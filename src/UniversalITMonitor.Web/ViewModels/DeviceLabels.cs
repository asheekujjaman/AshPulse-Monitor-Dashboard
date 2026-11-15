using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Web.ViewModels;

public static class DeviceLabels
{
    public static string Type(DeviceType type) => type switch
    {
        DeviceType.CoreSwitch => "Core Switch",
        DeviceType.DistributionSwitch => "Distribution Switch",
        DeviceType.AccessSwitch => "Access Switch",
        DeviceType.WirelessAccessPoint => "Wireless Access Point",
        DeviceType.Ups => "UPS",
        DeviceType.Workstation => "PC/Workstation",
        _ => type.ToString()
    };

    public static string MapIcon(MapIconKind icon) => icon switch
    {
        MapIconKind.Pc => "PC / Workstation",
        MapIconKind.Ups => "UPS",
        MapIconKind.VoipPhone => "VoIP Phone",
        MapIconKind.Nvr => "NVR",
        MapIconKind.Vpn => "VPN",
        MapIconKind.PowerDistributionUnit => "Power Distribution Unit",
        MapIconKind.CoreSwitch => "Core Switch",
        MapIconKind.DistributionSwitch => "Distribution Switch",
        MapIconKind.AccessSwitch => "Access Switch",
        MapIconKind.WirelessAccessPoint => "Wireless Access Point",
        MapIconKind.VirtualMachine => "Virtual Machine",
        MapIconKind.LoadBalancer => "Load Balancer",
        MapIconKind.SecuritySensor => "Security Sensor",
        _ => icon.ToString()
    };

    public static string MonitoringMethod(MonitoringMethod method) => method switch
    {
        UniversalITMonitor.Core.Models.MonitoringMethod.Icmp => "ICMP",
        UniversalITMonitor.Core.Models.MonitoringMethod.IcmpAndSnmp => "ICMP + SNMP",
        _ => method.ToString()
    };
}
