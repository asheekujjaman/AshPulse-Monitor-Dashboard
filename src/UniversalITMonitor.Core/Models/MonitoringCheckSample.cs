namespace UniversalITMonitor.Core.Models;

public sealed class MonitoringCheckSample
{
    public Guid DeviceId { get; set; }

    public DateTimeOffset CheckedUtc { get; set; }

    public DeviceStatus Status { get; set; }

    public long? ResponseTimeMilliseconds { get; set; }

    public string? Error { get; set; }
}

public sealed record MonitoringHistoryWindow(
    MonitoringHistoryEntry? BeforeStart,
    IReadOnlyList<MonitoringHistoryEntry> Transitions,
    DateTimeOffset? FirstRecordedUtc);

public sealed record MonitoringSampleWindow(
    IReadOnlyList<MonitoringCheckSample> Samples,
    MonitoringCheckSample? BeforeStart,
    DateTimeOffset? FirstRecordedUtc);
