namespace UniversalITMonitor.Core.Models;

public sealed class DeviceCheckBatchResult
{
    public IReadOnlyList<Guid> AppliedDeviceIds { get; init; } = [];

    public IReadOnlyList<MonitoringHistoryEntry> Transitions { get; init; } = [];
}
