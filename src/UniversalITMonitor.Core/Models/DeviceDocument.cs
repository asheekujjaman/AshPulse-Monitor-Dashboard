namespace UniversalITMonitor.Core.Models;

public sealed class DeviceDocument
{
    public List<NetworkDevice> Devices { get; set; } = [];

    // Written in the same atomic document update as the device's current status.
    // Consumers acknowledge an entry only after history and notification enqueue
    // have both completed idempotently.
    public List<MonitoringHistoryEntry> PendingTransitions { get; set; } = [];
}
