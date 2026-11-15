using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Core.Persistence;

public interface IMonitoringSampleStore
{
    Task AppendAsync(MonitoringCheckSample sample, CancellationToken cancellationToken = default);

    Task<MonitoringSampleWindow> GetWindowAsync(
        Guid deviceId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default);
}
