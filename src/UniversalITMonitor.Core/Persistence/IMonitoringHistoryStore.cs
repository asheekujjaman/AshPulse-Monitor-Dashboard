using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Core.Persistence;

public interface IMonitoringHistoryStore
{
    Task AppendAsync(MonitoringHistoryEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonitoringHistoryEntry>> GetForDeviceAsync(
        Guid deviceId,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<MonitoringHistoryWindow> GetWindowAsync(
        Guid deviceId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default);
}
