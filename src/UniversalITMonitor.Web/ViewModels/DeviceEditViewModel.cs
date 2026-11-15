using System.ComponentModel.DataAnnotations;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class DeviceEditViewModel
{
    public Guid? Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(45)]
    [Display(Name = "IP address")]
    public string IpAddress { get; set; } = string.Empty;

    [Display(Name = "Device type")]
    public DeviceType Type { get; set; } = DeviceType.Other;

    [Display(Name = "Monitoring method")]
    public MonitoringMethod MonitoringMethod { get; set; } = MonitoringMethod.Icmp;

    [Display(Name = "Location")]
    public Guid? LocationId { get; set; }

    [Display(Name = "Building")]
    public Guid? BuildingId { get; set; }

    [Display(Name = "Group")]
    public Guid? GroupId { get; set; }

    public IReadOnlyList<HierarchyChoice> LocationChoices { get; set; } = [];

    public IReadOnlyList<HierarchyChoice> BuildingChoices { get; set; } = [];

    public IReadOnlyList<HierarchyChoice> GroupChoices { get; set; } = [];

    [StringLength(120)]
    public string? Location { get; set; }

    [StringLength(120)]
    [Display(Name = "Group")]
    public string? Group { get; set; }

    [StringLength(120)]
    public string? Building { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    [Display(Name = "Monitoring enabled")]
    public bool MonitoringEnabled { get; set; } = true;

    [Range(1, 65535)]
    [Display(Name = "SNMP port")]
    public int SnmpPort { get; set; } = 161;

    [Display(Name = "SNMP version")]
    public SnmpVersion SnmpVersion { get; set; } = SnmpVersion.V2c;

    [Display(Name = "Community string")]
    public string? SnmpCommunity { get; set; }

    [Display(Name = "SNMP username")]
    public string? SnmpUserName { get; set; }

    [Display(Name = "Security level")]
    public SnmpSecurityLevel SnmpSecurityLevel { get; set; } = SnmpSecurityLevel.AuthPriv;

    [Display(Name = "Authentication protocol")]
    public SnmpAuthenticationProtocol SnmpAuthenticationProtocol { get; set; } = SnmpAuthenticationProtocol.Sha256;

    [DataType(DataType.Password)]
    [Display(Name = "Authentication password")]
    public string? SnmpAuthenticationPassword { get; set; }

    [Display(Name = "Privacy protocol")]
    public SnmpPrivacyProtocol SnmpPrivacyProtocol { get; set; } = SnmpPrivacyProtocol.Aes128;

    [DataType(DataType.Password)]
    [Display(Name = "Privacy password")]
    public string? SnmpPrivacyPassword { get; set; }

    public bool IsEditing => Id.HasValue;

    public bool HasSnmpCommunity { get; set; }

    public bool HasSnmpAuthenticationPassword { get; set; }

    public bool HasSnmpPrivacyPassword { get; set; }

    public static DeviceEditViewModel FromInfo(DeviceInfo device) => new()
    {
        Id = device.Id,
        Name = device.Name,
        IpAddress = device.IpAddress,
        Type = device.Type,
        MonitoringMethod = device.MonitoringMethod,
        LocationId = device.LocationId,
        BuildingId = device.BuildingId,
        GroupId = device.GroupId,
        Location = device.Location,
        Group = device.Group,
        Building = device.Building,
        Description = device.Description,
        MonitoringEnabled = device.MonitoringEnabled,
        SnmpPort = device.SnmpPort,
        SnmpVersion = device.SnmpVersion,
        SnmpUserName = device.SnmpUserName,
        SnmpSecurityLevel = device.SnmpSecurityLevel,
        SnmpAuthenticationProtocol = device.SnmpAuthenticationProtocol,
        SnmpPrivacyProtocol = device.SnmpPrivacyProtocol,
        HasSnmpCommunity = device.HasSnmpCommunity,
        HasSnmpAuthenticationPassword = device.HasSnmpAuthenticationPassword,
        HasSnmpPrivacyPassword = device.HasSnmpPrivacyPassword
    };

    public DeviceEditInput ToInput() => new()
    {
        Id = Id,
        Name = Name,
        IpAddress = IpAddress,
        Type = Type,
        MonitoringMethod = MonitoringMethod,
        LocationId = LocationId,
        BuildingId = BuildingId,
        GroupId = GroupId,
        Location = Location,
        Group = Group,
        Building = Building,
        Description = Description,
        MonitoringEnabled = MonitoringEnabled,
        SnmpPort = SnmpPort,
        SnmpVersion = SnmpVersion,
        SnmpCommunity = SnmpCommunity,
        SnmpUserName = SnmpUserName,
        SnmpSecurityLevel = SnmpSecurityLevel,
        SnmpAuthenticationProtocol = SnmpAuthenticationProtocol,
        SnmpAuthenticationPassword = SnmpAuthenticationPassword,
        SnmpPrivacyProtocol = SnmpPrivacyProtocol,
        SnmpPrivacyPassword = SnmpPrivacyPassword
    };
}
