using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Core.Monitoring;

public interface ISnmpMetricsCollector
{
    Task<SnmpMetrics> CollectAsync(NetworkDevice device, CancellationToken cancellationToken = default);
}
