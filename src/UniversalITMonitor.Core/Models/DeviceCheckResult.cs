namespace UniversalITMonitor.Core.Models;

public sealed class DeviceCheckResult
{
    public Guid DeviceId { get; set; }

    public string IpAddress { get; set; } = string.Empty;

    public DateTimeOffset CheckedUtc { get; set; }

    public DeviceStatus Status { get; set; }

    public long? ResponseTimeMilliseconds { get; set; }

    public string? Error { get; set; }

    public SnmpMetrics? SnmpMetrics { get; set; }
}
