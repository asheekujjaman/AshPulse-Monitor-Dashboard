using System.Globalization;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Notifications;

public sealed class NotificationService(
    INotificationRepository repository,
    INotificationSecretProtector secretProtector,
    IDeviceService devices,
    ITelegramNotificationSender telegram,
    IEmailNotificationSender email,
    ILogger<NotificationService> logger) : INotificationService
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30)
    ];
    private readonly SemaphoreSlim _deliveryGate = new(1, 1);

    public async Task<NotificationSettingsView> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        ToView(await repository.GetSettingsAsync(cancellationToken));

    public async Task<NotificationOperationResult> SaveSettingsAsync(
        NotificationSettingsInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var existing = await repository.GetSettingsAsync(cancellationToken);
        var hasBotToken = !string.IsNullOrWhiteSpace(input.TelegramBotToken) ||
            !input.ClearTelegramBotToken && !string.IsNullOrWhiteSpace(existing.ProtectedTelegramBotToken);
        var hasSmtpUserName = !string.IsNullOrWhiteSpace(input.SmtpUserName) ||
            !input.ClearSmtpCredentials && !string.IsNullOrWhiteSpace(existing.ProtectedSmtpUserName);
        var hasSmtpPassword = !string.IsNullOrWhiteSpace(input.SmtpPassword) ||
            !input.ClearSmtpCredentials && !string.IsNullOrWhiteSpace(existing.ProtectedSmtpPassword);
        var chatId = TrimOrNull(input.TelegramChatId);
        var host = TrimOrNull(input.SmtpHost);
        var from = TrimOrNull(input.SmtpFromAddress);
        var to = TrimOrNull(input.SmtpToAddress);

        if (input.TelegramEnabled && (!hasBotToken || string.IsNullOrWhiteSpace(chatId)))
        {
            return new(false, "Enter a Telegram bot token and chat ID before enabling Telegram notifications.");
        }

        if (!string.IsNullOrWhiteSpace(chatId) && chatId.Length > 128)
        {
            return new(false, "The Telegram chat ID must be 128 characters or fewer.");
        }

        if (input.EmailEnabled)
        {
            if (string.IsNullOrWhiteSpace(host) || host.Length > 255 || input.SmtpPort is < 1 or > 65535 ||
                !IsEmailAddress(from) || !IsEmailAddress(to))
            {
                return new(false, "Enter a valid SMTP host, port, sender address, and recipient address before enabling email.");
            }

            if (!input.SmtpUseTls)
            {
                return new(false, "Email notifications require SMTP TLS.");
            }

            if (hasSmtpUserName != hasSmtpPassword)
            {
                return new(false, "Enter both SMTP credentials, or leave both empty for an unauthenticated relay.");
            }
        }

        var settings = new NotificationSettings
        {
            TelegramEnabled = input.TelegramEnabled,
            TelegramChatId = chatId,
            ProtectedTelegramBotToken = ProtectIfPlaintext(input.TelegramBotToken, input.ClearTelegramBotToken, existing.ProtectedTelegramBotToken),
            EmailEnabled = input.EmailEnabled,
            SmtpHost = host,
            SmtpPort = input.SmtpPort is < 1 or > 65535 ? 587 : input.SmtpPort,
            SmtpFromAddress = from,
            SmtpToAddress = to,
            SmtpUseTls = input.SmtpUseTls,
            ProtectedSmtpUserName = ProtectIfPlaintext(input.SmtpUserName, input.ClearSmtpCredentials, existing.ProtectedSmtpUserName),
            ProtectedSmtpPassword = ProtectIfPlaintext(input.SmtpPassword, input.ClearSmtpCredentials, existing.ProtectedSmtpPassword)
        };
        await repository.SaveSettingsAsync(settings, cancellationToken);
        return new(true);
    }

    public async Task<NotificationSendResult> TestSendAsync(NotificationChannel channel, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(channel)) return new(false, "Choose a supported notification channel.");
        var settings = await repository.GetSettingsAsync(cancellationToken);
        var record = new NotificationRecord
        {
            Kind = NotificationRecordKind.Test,
            DeviceName = "Notification test",
            IpAddress = "Not applicable",
            Status = DeviceStatus.Unknown,
            OccurredUtc = DateTimeOffset.UtcNow,
            Details = "Administrator requested a delivery test.",
            RequestedChannels = [channel]
        };

        await repository.EnqueueAsync(record, cancellationToken);
        await ProcessPendingAsync(cancellationToken);
        var completed = (await repository.GetRecentAsync(200, cancellationToken)).FirstOrDefault(item => item.Id == record.Id);
        var result = completed?.Deliveries.FirstOrDefault(item => item.Channel == channel);
        return result is null
            ? new(false, "The test message remains queued; check notification settings and delivery history shortly.")
            : new(result.Succeeded, result.Succeeded ? "Test message sent." : result.Detail ?? "Delivery failed.");
    }

    public async Task QueueTransitionAsync(MonitoringHistoryEntry transition, CancellationToken cancellationToken = default)
    {
        if (transition.Status == DeviceStatus.Unknown ||
            transition.Status == DeviceStatus.Up && transition.PreviousStatus != DeviceStatus.Down)
        {
            return;
        }
        var device = await devices.GetAsync(transition.DeviceId, cancellationToken);
        if (device is null && string.IsNullOrWhiteSpace(transition.DeviceName)) return;
        var settings = await repository.GetSettingsAsync(cancellationToken);
        var record = new NotificationRecord
        {
            Id = transition.Id,
            Kind = NotificationRecordKind.DeviceTransition,
            DeviceId = transition.DeviceId,
            DeviceName = transition.DeviceName ?? device?.Name,
            IpAddress = transition.IpAddress ?? device?.IpAddress,
            Location = transition.Location ?? device?.Location,
            Group = transition.Group ?? device?.Group,
            PreviousStatus = transition.PreviousStatus,
            Status = transition.Status,
            OccurredUtc = transition.OccurredUtc,
            ResponseTimeMilliseconds = transition.ResponseTimeMilliseconds,
            Details = transition.Status == DeviceStatus.Down
                ? TrimOrNull(transition.Error) ?? "The device did not respond to the monitoring check."
                : "The device responded to the monitoring check.",
            RequestedChannels = GetEnabledChannels(settings)
        };

        await repository.EnqueueAsync(record, cancellationToken);
    }

    public async Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        await _deliveryGate.WaitAsync(cancellationToken);
        try
        {
            var settings = await repository.GetSettingsAsync(cancellationToken);
            var pending = await repository.GetPendingAsync(cancellationToken);
            foreach (var record in pending)
            {
                foreach (var channel in record.RequestedChannels)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var latest = LatestAttempt(record, channel);
                    if (latest is { Succeeded: true } or { DeadLettered: true } ||
                        latest?.NextAttemptUtc is { } nextAttempt && nextAttempt > DateTimeOffset.UtcNow)
                    {
                        continue;
                    }

                    await DeliverOneAsync(record, channel, settings, latest?.AttemptNumber ?? 0, cancellationToken);
                }
            }
        }
        finally
        {
            _deliveryGate.Release();
        }
    }

    public Task<IReadOnlyList<NotificationRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default) =>
        repository.GetRecentAsync(limit, cancellationToken);

    public Task<bool> RetryDeadLetterAsync(Guid recordId, NotificationChannel channel, CancellationToken cancellationToken = default) =>
        repository.RetryDeadLetterAsync(recordId, channel, cancellationToken);

    private async Task DeliverOneAsync(
        NotificationRecord record,
        NotificationChannel channel,
        NotificationSettings settings,
        int previousAttemptCount,
        CancellationToken cancellationToken)
    {
        NotificationTransportResult transport;
        try
        {
            var message = CreateMessage(record);
            transport = channel switch
            {
                NotificationChannel.Telegram => await telegram.SendAsync(
                    UnprotectOrNull(settings.ProtectedTelegramBotToken) ?? string.Empty,
                    settings.TelegramChatId ?? string.Empty,
                    message.Body,
                    cancellationToken),
                NotificationChannel.Email => await email.SendAsync(
                    settings,
                    UnprotectOrNull(settings.ProtectedSmtpUserName),
                    UnprotectOrNull(settings.ProtectedSmtpPassword),
                    message,
                    cancellationToken),
                _ => new(false, "Unsupported notification channel.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Notification delivery failed on {Channel} ({FailureType}).", channel, exception.GetType().Name);
            transport = new(false, channel == NotificationChannel.Telegram
                ? "Telegram delivery failed. Check the bot token, chat ID, and network access."
                : "Email delivery failed. Check the SMTP settings and network access.",
                IsTransientException(exception));
        }

        var attemptNumber = previousAttemptCount + 1;
        var deadLettered = !transport.Succeeded && (!transport.IsTransient || attemptNumber > RetryDelays.Length);
        DateTimeOffset? nextAttemptUtc = !transport.Succeeded && transport.IsTransient && !deadLettered
            ? DateTimeOffset.UtcNow.Add(RetryDelays[Math.Min(attemptNumber - 1, RetryDelays.Length - 1)])
            : null;

        var result = new NotificationDeliveryResult
        {
            Channel = channel,
            AttemptedUtc = DateTimeOffset.UtcNow,
            AttemptNumber = attemptNumber,
            Succeeded = transport.Succeeded,
            IsTransient = transport.IsTransient,
            DeadLettered = deadLettered,
            NextAttemptUtc = nextAttemptUtc,
            Detail = deadLettered && transport.IsTransient
                ? "Maximum automatic attempts reached. Administrator retry is available."
                : transport.Detail
        };
        await repository.RecordDeliveryAsync(record.Id, result, cancellationToken);
    }

    private static NotificationDeliveryResult? LatestAttempt(NotificationRecord record, NotificationChannel channel) =>
        record.Deliveries.Where(delivery => delivery.Channel == channel)
            .OrderByDescending(delivery => delivery.AttemptedUtc).FirstOrDefault();

    private static bool IsTransientException(Exception exception) => exception is
        HttpRequestException or TimeoutException or OperationCanceledException or
        System.Net.Sockets.SocketException or IOException or System.Net.Mail.SmtpException;

    private NotificationMessage CreateMessage(NotificationRecord record)
    {
        var status = record.Status?.ToString().ToUpperInvariant() ?? "UNKNOWN";
        var title = record.Kind == NotificationRecordKind.Test
            ? "AshPulse notification test"
            : $"{record.DeviceName ?? "Device"} is {status}";
        var body = new StringBuilder()
            .AppendLine(title)
            .AppendLine($"Device: {record.DeviceName ?? "Unavailable"}")
            .AppendLine($"IP address: {record.IpAddress ?? "Unavailable"}")
            .AppendLine($"Status: {status}");
        if (record.PreviousStatus is { } previous) body.AppendLine($"Previous status: {previous}");
        body.AppendLine($"Location: {record.Location ?? "Unassigned"}");
        if (!string.IsNullOrWhiteSpace(record.Group)) body.AppendLine($"Group: {record.Group}");
        if (record.ResponseTimeMilliseconds is { } response) body.AppendLine($"Response time: {response.ToString(CultureInfo.InvariantCulture)} ms");
        body.AppendLine($"Timestamp (UTC): {record.OccurredUtc.ToUniversalTime():yyyy-MM-dd HH:mm:ss 'UTC'}");
        if (!string.IsNullOrWhiteSpace(record.Details)) body.AppendLine($"Details: {record.Details}");
        return new(title, body.ToString());
    }

    private NotificationSettingsView ToView(NotificationSettings settings) => new(
        settings.TelegramEnabled,
        settings.TelegramChatId,
        !string.IsNullOrWhiteSpace(settings.ProtectedTelegramBotToken),
        settings.EmailEnabled,
        settings.SmtpHost,
        settings.SmtpPort,
        settings.SmtpFromAddress,
        settings.SmtpToAddress,
        settings.SmtpUseTls,
        !string.IsNullOrWhiteSpace(settings.ProtectedSmtpUserName),
        !string.IsNullOrWhiteSpace(settings.ProtectedSmtpPassword));

    private string? UnprotectOrNull(string? protectedValue) =>
        string.IsNullOrWhiteSpace(protectedValue) ? null : secretProtector.Unprotect(protectedValue);

    private string? ProtectIfPlaintext(string? submitted, bool clear, string? existing) =>
        !string.IsNullOrWhiteSpace(submitted) ? secretProtector.Protect(submitted.Trim()) : clear ? null : existing;

    private static bool IsEmailAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254) return false;
        try
        {
            return string.Equals(new MailAddress(value.Trim()).Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static List<NotificationChannel> GetEnabledChannels(NotificationSettings settings)
    {
        var channels = new List<NotificationChannel>();
        if (settings.TelegramEnabled) channels.Add(NotificationChannel.Telegram);
        if (settings.EmailEnabled) channels.Add(NotificationChannel.Email);
        return channels;
    }

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
