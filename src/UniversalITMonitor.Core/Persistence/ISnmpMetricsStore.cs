using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Core.Persistence;

public interface ISnmpMetricsStore
{
    Task<IReadOnlyDictionary<Guid, SnmpMetrics>> GetManyAsync(IEnumerable<Guid> deviceIds, CancellationToken cancellationToken = default);

    Task WriteAsync(Guid deviceId, SnmpMetrics metrics, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid deviceId, CancellationToken cancellationToken = default);
}
