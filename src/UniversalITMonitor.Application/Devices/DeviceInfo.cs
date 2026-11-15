using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Application.Devices;

public sealed class DeviceInfo
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string IpAddress { get; init; } = string.Empty;

    public DeviceType Type { get; init; }

    public MonitoringMethod MonitoringMethod { get; init; }

    public Guid? LocationId { get; init; }

    public Guid? BuildingId { get; init; }

    public Guid? GroupId { get; init; }

    public string? Location { get; init; }

    public string? Group { get; init; }

    public string? Building { get; init; }

    public string? Description { get; init; }

    public bool MonitoringEnabled { get; init; }

    public SnmpVersion SnmpVersion { get; init; }

    public int SnmpPort { get; init; }

    public string? SnmpUserName { get; init; }

    public SnmpSecurityLevel SnmpSecurityLevel { get; init; }

    public SnmpAuthenticationProtocol SnmpAuthenticationProtocol { get; init; }

    public SnmpPrivacyProtocol SnmpPrivacyProtocol { get; init; }

    public bool HasSnmpCommunity { get; init; }

    public bool HasSnmpAuthenticationPassword { get; init; }

    public bool HasSnmpPrivacyPassword { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }

    public DeviceStatus Status { get; init; }

    public DateTimeOffset? LastCheckUtc { get; init; }

    public DateTimeOffset? LastSuccessfulCheckUtc { get; init; }

    public DateTimeOffset? LastFailedCheckUtc { get; init; }

    public DateTimeOffset? CurrentStateSinceUtc { get; init; }

    public long? ResponseTimeMilliseconds { get; init; }

    public string? LastCheckError { get; init; }

    public SnmpMetrics? LatestSnmpMetrics { get; init; }
}
