using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Storage.Json;

public sealed class JsonNotificationRepository(IJsonDocumentStore documents) : INotificationRepository
{
    private const int HistoryLimit = 2000;

    public async Task<NotificationSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var document = await documents.ReadAsync("notification-settings.json", static () => new NotificationSettingsDocument(), cancellationToken);
        return document.Settings;
    }

    public Task SaveSettingsAsync(NotificationSettings settings, CancellationToken cancellationToken = default) =>
        documents.WriteAsync("notification-settings.json", new NotificationSettingsDocument { Settings = settings }, cancellationToken);

    public Task<bool> EnqueueAsync(NotificationRecord record, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<NotificationHistoryDocument, bool>(
            "notification-history.json",
            static () => new NotificationHistoryDocument(),
            document =>
            {
                if (document.Records.Any(existing => existing.Id == record.Id))
                {
                    return (document, false);
                }

                record.IsComplete = record.RequestedChannels.Count == 0;
                document.Records.Add(record);
                TrimCompletedRecords(document);
                return (document, true);
            },
            cancellationToken);

    public Task<bool> EnqueueRecoveryAsync(NotificationRecord record, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<NotificationHistoryDocument, bool>(
            "notification-history.json",
            static () => new NotificationHistoryDocument(),
            document =>
            {
                if (document.Records.Any(existing => existing.Id == record.Id)) return (document, false);

                var previousDown = document.Records
                    .Where(existing => existing.Kind == NotificationRecordKind.DeviceTransition &&
                        existing.DeviceId == record.DeviceId && existing.Status == DeviceStatus.Down &&
                        existing.OccurredUtc <= record.OccurredUtc)
                    .OrderByDescending(existing => existing.OccurredUtc)
                    .FirstOrDefault();

                if (previousDown is not null)
                {
                    if (previousDown.IsSuppressed) return (document, false);
                    if (previousDown.RequestedChannels.Count == 0) return (document, false);

                    if (previousDown.NotBeforeUtc is { } dueUtc && record.OccurredUtc < dueUtc &&
                        previousDown.Deliveries.Count == 0)
                    {
                        previousDown.IsSuppressed = true;
                        previousDown.IsComplete = true;
                        TrimCompletedRecords(document);
                        return (document, false);
                    }

                    record.DependsOnRecordId = previousDown.Id;
                }

                record.IsComplete = record.RequestedChannels.Count == 0;
                document.Records.Add(record);
                TrimCompletedRecords(document);
                return (document, true);
            },
            cancellationToken);

    public async Task<IReadOnlyList<NotificationRecord>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        var document = await documents.ReadAsync("notification-history.json", static () => new NotificationHistoryDocument(), cancellationToken);
        var completedIds = document.Records.Where(record => record.IsComplete).Select(record => record.Id).ToHashSet();
        var existingIds = document.Records.Select(record => record.Id).ToHashSet();
        return document.Records
            .Where(record => !record.IsComplete &&
                (record.DependsOnRecordId is null || !existingIds.Contains(record.DependsOnRecordId.Value) ||
                    completedIds.Contains(record.DependsOnRecordId.Value)))
            .OrderBy(record => record.OccurredUtc)
            .ToArray();
    }

    public async Task<IReadOnlyList<NotificationRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var document = await documents.ReadAsync("notification-history.json", static () => new NotificationHistoryDocument(), cancellationToken);
        return document.Records.OrderByDescending(record => record.OccurredUtc).Take(Math.Clamp(limit, 1, 500)).ToArray();
    }

    public Task<bool> RecordDeliveryAsync(
        Guid recordId,
        NotificationDeliveryResult result,
        CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<NotificationHistoryDocument, bool>(
            "notification-history.json",
            static () => new NotificationHistoryDocument(),
            document =>
            {
                var record = document.Records.FirstOrDefault(existing => existing.Id == recordId);
                if (record is null || record.Deliveries.Any(delivery => delivery.Id == result.Id))
                {
                    return (document, false);
                }

                record.Deliveries.Add(result);
                record.IsComplete = record.RequestedChannels.All(channel => IsTerminal(record, channel));
                TrimCompletedRecords(document);
                return (document, true);
            },
            cancellationToken);

    public Task<bool> RetryDeadLetterAsync(
        Guid recordId,
        NotificationChannel channel,
        CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<NotificationHistoryDocument, bool>(
            "notification-history.json",
            static () => new NotificationHistoryDocument(),
            document =>
            {
                var record = document.Records.FirstOrDefault(existing => existing.Id == recordId);
                var latest = record?.Deliveries.Where(delivery => delivery.Channel == channel)
                    .OrderByDescending(delivery => delivery.AttemptedUtc).FirstOrDefault();
                if (record is null || latest is not { DeadLettered: true, Succeeded: false }) return (document, false);
                latest.DeadLettered = false;
                latest.AttemptNumber = 0;
                latest.NextAttemptUtc = DateTimeOffset.UtcNow;
                record.IsComplete = false;
                return (document, true);
            },
            cancellationToken);

    private static void TrimCompletedRecords(NotificationHistoryDocument document)
    {
        var completed = document.Records.Where(record => record.IsComplete)
            .OrderByDescending(record => record.OccurredUtc)
            .Take(HistoryLimit)
            .Select(record => record.Id)
            .ToHashSet();
        document.Records = document.Records.Where(record => !record.IsComplete || completed.Contains(record.Id)).ToList();
    }

    private static bool IsTerminal(NotificationRecord record, NotificationChannel channel)
    {
        var latest = record.Deliveries.Where(delivery => delivery.Channel == channel)
            .OrderByDescending(delivery => delivery.AttemptedUtc).FirstOrDefault();
        return latest is { Succeeded: true } or { DeadLettered: true };
    }
}
