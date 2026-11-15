namespace UniversalITMonitor.Core.Models;

public sealed class MonitoringHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DeviceId { get; set; }

    public string? DeviceName { get; set; }

    public string? IpAddress { get; set; }

    public string? Location { get; set; }

    public string? Group { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    public DeviceStatus PreviousStatus { get; set; }

    public DeviceStatus Status { get; set; }

    public long? ResponseTimeMilliseconds { get; set; }

    public string? Error { get; set; }
}
