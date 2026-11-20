using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Core.Persistence;

public interface INotificationRepository
{
    Task<NotificationSettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(NotificationSettings settings, CancellationToken cancellationToken = default);

    Task<bool> EnqueueAsync(NotificationRecord record, CancellationToken cancellationToken = default);

    Task<bool> EnqueueRecoveryAsync(NotificationRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationRecord>> GetPendingAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<bool> RecordDeliveryAsync(Guid recordId, NotificationDeliveryResult result, CancellationToken cancellationToken = default);

    Task<bool> RetryDeadLetterAsync(Guid recordId, NotificationChannel channel, CancellationToken cancellationToken = default);
}
