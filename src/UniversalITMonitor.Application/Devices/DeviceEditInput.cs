using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Application.Devices;

public sealed class DeviceEditInput
{
    public Guid? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public DeviceType Type { get; set; } = DeviceType.Other;

    public MonitoringMethod MonitoringMethod { get; set; } = MonitoringMethod.Icmp;

    public Guid? LocationId { get; set; }

    public Guid? BuildingId { get; set; }

    public Guid? GroupId { get; set; }

    public string? Location { get; set; }

    public string? Group { get; set; }

    public string? Building { get; set; }

    public string? Description { get; set; }

    public bool MonitoringEnabled { get; set; } = true;

    public int SnmpPort { get; set; } = 161;

    public SnmpVersion SnmpVersion { get; set; } = SnmpVersion.V2c;

    public string? SnmpCommunity { get; set; }

    public string? SnmpUserName { get; set; }

    public SnmpSecurityLevel SnmpSecurityLevel { get; set; } = SnmpSecurityLevel.AuthPriv;

    public SnmpAuthenticationProtocol SnmpAuthenticationProtocol { get; set; } = SnmpAuthenticationProtocol.Sha256;

    public string? SnmpAuthenticationPassword { get; set; }

    public SnmpPrivacyProtocol SnmpPrivacyProtocol { get; set; } = SnmpPrivacyProtocol.Aes128;

    public string? SnmpPrivacyPassword { get; set; }
}
