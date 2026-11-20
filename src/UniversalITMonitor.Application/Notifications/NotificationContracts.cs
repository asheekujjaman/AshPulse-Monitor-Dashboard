using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Application.Notifications;

public sealed class NotificationSettingsInput
{
    public bool TelegramEnabled { get; set; }
    public string? TelegramChatId { get; set; }
    public string? TelegramBotToken { get; set; }
    public bool ClearTelegramBotToken { get; set; }
    public bool EmailEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpFromAddress { get; set; }
    public string? SmtpToAddress { get; set; }
    public bool SmtpUseTls { get; set; } = true;
    public string? SmtpUserName { get; set; }
    public string? SmtpPassword { get; set; }
    public bool ClearSmtpCredentials { get; set; }
}

public sealed record NotificationSettingsView(
    bool TelegramEnabled,
    string? TelegramChatId,
    bool HasTelegramBotToken,
    bool EmailEnabled,
    string? SmtpHost,
    int SmtpPort,
    string? SmtpFromAddress,
    string? SmtpToAddress,
    bool SmtpUseTls,
    bool HasSmtpUserName,
    bool HasSmtpPassword);

public sealed record NotificationOperationResult(bool Success, string? Error = null);

public sealed record NotificationSendResult(bool Success, string Message);

public sealed record NotificationMessage(string Subject, string Body);

public sealed record NotificationTransportResult(
    bool Succeeded,
    string? Detail = null,
    bool IsTransient = false,
    TimeSpan? RetryAfter = null);

public interface INotificationSecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}

public interface ITelegramNotificationSender
{
    Task<NotificationTransportResult> SendAsync(string botToken, string chatId, string text, CancellationToken cancellationToken = default);
}

public interface IEmailNotificationSender
{
    Task<NotificationTransportResult> SendAsync(
        NotificationSettings settings,
        string? userName,
        string? password,
        NotificationMessage message,
        CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task<NotificationSettingsView> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<NotificationOperationResult> SaveSettingsAsync(NotificationSettingsInput input, CancellationToken cancellationToken = default);
    Task<NotificationSendResult> TestSendAsync(NotificationChannel channel, CancellationToken cancellationToken = default);
    Task QueueTransitionAsync(MonitoringHistoryEntry transition, CancellationToken cancellationToken = default);
    Task ProcessPendingAsync(CancellationToken cancellationToken = default);

    Task<bool> RetryDeadLetterAsync(Guid recordId, NotificationChannel channel, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default);
}
