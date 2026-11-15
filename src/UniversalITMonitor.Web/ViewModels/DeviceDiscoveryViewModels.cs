using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class DeviceDiscoveryViewModel
{
    public string? SubnetCidr { get; set; }
    public int TimeoutMilliseconds { get; set; } = 500;
    public IReadOnlyList<HierarchyChoice> LocationChoices { get; set; } = [];
    public IReadOnlyList<HierarchyChoice> BuildingChoices { get; set; } = [];
    public IReadOnlyList<HierarchyChoice> GroupChoices { get; set; } = [];
    public IReadOnlyList<SubnetChoice> RegisteredSubnets { get; set; } = [];
}

public sealed record SubnetChoice(string Cidr, string? Location, string? Description);

public sealed class SubnetScanRequest
{
    public string Cidr { get; set; } = string.Empty;
    public int TimeoutMilliseconds { get; set; } = 500;
}

public sealed class DiscoveredDeviceItem
{
    public string IpAddress { get; set; } = string.Empty;
    public long ResponseTimeMilliseconds { get; set; }
    public string? Hostname { get; set; }
    public string SuggestedName { get; set; } = string.Empty;
    public string SuggestedType { get; set; } = "Other";
    public bool AlreadyInInventory { get; set; }
    public Guid? ExistingDeviceId { get; set; }
}

public sealed class SubnetScanResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string Cidr { get; set; } = string.Empty;
    public int TotalScanned { get; set; }
    public int ResponsiveCount { get; set; }
    public List<DiscoveredDeviceItem> Devices { get; set; } = [];
}

public sealed class SingleAddDiscoveredRequest
{
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DeviceType Type { get; set; } = DeviceType.Other;
    public MonitoringMethod MonitoringMethod { get; set; } = MonitoringMethod.Icmp;
    public Guid? LocationId { get; set; }
    public Guid? BuildingId { get; set; }
    public Guid? GroupId { get; set; }
    public string? Description { get; set; }
    public bool MonitoringEnabled { get; set; } = true;
    public SnmpVersion SnmpVersion { get; set; } = SnmpVersion.V2c;
    public int SnmpPort { get; set; } = 161;
    public string? SnmpCommunity { get; set; }
    public string? SnmpUserName { get; set; }
    public SnmpSecurityLevel SnmpSecurityLevel { get; set; } = SnmpSecurityLevel.AuthPriv;
    public SnmpAuthenticationProtocol SnmpAuthenticationProtocol { get; set; } = SnmpAuthenticationProtocol.Sha256;
    public string? SnmpAuthenticationPassword { get; set; }
    public SnmpPrivacyProtocol SnmpPrivacyProtocol { get; set; } = SnmpPrivacyProtocol.Aes128;
    public string? SnmpPrivacyPassword { get; set; }
}

public sealed class BatchAddDiscoveredRequest
{
    public List<DiscoveredDeviceAddItem> Devices { get; set; } = [];

    // Retained for compatibility with clients that submit only IP addresses.
    public List<string> IpAddresses { get; set; } = [];
    public DeviceType CommonType { get; set; } = DeviceType.Other;
    public MonitoringMethod CommonMonitoringMethod { get; set; } = MonitoringMethod.Icmp;
    public bool MonitoringEnabled { get; set; } = true;
    public Guid? LocationId { get; set; }
    public Guid? BuildingId { get; set; }
    public Guid? GroupId { get; set; }
    public string? Description { get; set; }
    public SnmpVersion SnmpVersion { get; set; } = SnmpVersion.V2c;
    public string? SnmpCommunity { get; set; }
    public string? SnmpUserName { get; set; }
    public SnmpSecurityLevel SnmpSecurityLevel { get; set; } = SnmpSecurityLevel.AuthPriv;
    public SnmpAuthenticationProtocol SnmpAuthenticationProtocol { get; set; } = SnmpAuthenticationProtocol.Sha256;
    public string? SnmpAuthenticationPassword { get; set; }
    public SnmpPrivacyProtocol SnmpPrivacyProtocol { get; set; } = SnmpPrivacyProtocol.Aes128;
    public string? SnmpPrivacyPassword { get; set; }
    public int SnmpPort { get; set; } = 161;
}

public sealed class DiscoveredDeviceAddItem
{
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string? DeviceType { get; set; }
}

public sealed class AddDiscoveredResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public int AddedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> AddedIpAddresses { get; set; } = [];
    public List<string> SkippedIpAddresses { get; set; } = [];
}

public class IpamDiscoveredAddRequest
{
    public string IpAddress { get; set; } = string.Empty;
    public string? SubnetCidr { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceType { get; set; }
    public IpAssignmentStatus AssignmentStatus { get; set; } = IpAssignmentStatus.Assigned;
    public int? VlanId { get; set; }
    public string? VlanName { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? BuildingId { get; set; }
    public Guid? GroupId { get; set; }
    public string? Description { get; set; }
    public string? SwitchName { get; set; }
    public string? SwitchIpAddress { get; set; }
    public string? SwitchPort { get; set; }
    public string? MacAddress { get; set; }
    public string? AssignedTo { get; set; }
    public string? Department { get; set; }
}

public sealed class IpamDiscoveredBatchRequest : IpamDiscoveredAddRequest
{
    public List<DiscoveredDeviceAddItem> Devices { get; set; } = [];
}

