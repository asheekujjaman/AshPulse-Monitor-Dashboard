using System.Text.Json.Serialization;

namespace UniversalITMonitor.Core.Models;

public sealed class NetworkDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public DeviceType Type { get; set; } = DeviceType.Other;

    public MonitoringMethod MonitoringMethod { get; set; } = MonitoringMethod.Icmp;

    public string? Location { get; set; }

    public Guid? LocationId { get; set; }

    public string? Group { get; set; }

    public Guid? GroupId { get; set; }

    public string? Building { get; set; }

    public Guid? BuildingId { get; set; }

    public string? Description { get; set; }

    public bool MonitoringEnabled { get; set; } = true;

    public SnmpConfiguration Snmp { get; set; } = new();

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;

    public DateTimeOffset? LastCheckUtc { get; set; }

    public DateTimeOffset? LastSuccessfulCheckUtc { get; set; }

    public DateTimeOffset? LastFailedCheckUtc { get; set; }

    public DateTimeOffset? CurrentStateSinceUtc { get; set; }

    public long? ResponseTimeMilliseconds { get; set; }

    public string? LastCheckError { get; set; }

    [JsonIgnore]
    public SnmpMetrics? LatestSnmpMetrics { get; set; }
}

public sealed class SnmpConfiguration
{
    public int Port { get; set; } = 161;

    public SnmpVersion Version { get; set; } = SnmpVersion.V2c;

    // Secrets are protected with ASP.NET Core Data Protection before persistence.
    public string? ProtectedCommunity { get; set; }

    public string? UserName { get; set; }

    public SnmpSecurityLevel SecurityLevel { get; set; } = SnmpSecurityLevel.AuthPriv;

    public SnmpAuthenticationProtocol AuthenticationProtocol { get; set; } = SnmpAuthenticationProtocol.Sha256;

    public string? ProtectedAuthenticationPassword { get; set; }

    public SnmpPrivacyProtocol PrivacyProtocol { get; set; } = SnmpPrivacyProtocol.Aes128;

    public string? ProtectedPrivacyPassword { get; set; }
}

public sealed class SnmpMetrics
{
    public DateTimeOffset CollectedUtc { get; set; }

    public string? CollectionError { get; set; }

    public string? SystemDescription { get; set; }

    public string? SystemName { get; set; }

    public uint? SystemUptimeHundredths { get; set; }

    public double? CpuUtilizationPercent { get; set; }

    public double? MemoryUtilizationPercent { get; set; }

    public List<SnmpInterfaceMetric> Interfaces { get; set; } = [];
}

public sealed class SnmpInterfaceMetric
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int? OperationalStatus { get; set; }

    public int? AdminStatus { get; set; }

    public int? VlanId { get; set; }

    public ulong? InOctets { get; set; }

    public ulong? OutOctets { get; set; }

    public double? InBitsPerSecond { get; set; }

    public double? OutBitsPerSecond { get; set; }

    public long? SpeedBitsPerSecond { get; set; }
}
