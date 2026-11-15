using System.ComponentModel.DataAnnotations;

namespace UniversalITMonitor.Core.Models;

public enum DeviceType
{
    Router,
    [Display(Name = "Core Switch")]
    CoreSwitch,
    [Display(Name = "Distribution Switch")]
    DistributionSwitch,
    [Display(Name = "Access Switch")]
    AccessSwitch,
    Firewall,
    [Display(Name = "Wireless Access Point")]
    WirelessAccessPoint,
    Server,
    Printer,
    Storage,
    [Display(Name = "UPS")]
    Ups,
    [Display(Name = "PC/Workstation")]
    Workstation,
    Other
}

public enum MonitoringMethod
{
    [Display(Name = "ICMP")]
    Icmp,
    [Display(Name = "ICMP + SNMP")]
    IcmpAndSnmp
}

public enum DeviceStatus
{
    Unknown,
    Up,
    Down
}

public enum SnmpVersion
{
    [Display(Name = "SNMP v2c")]
    V2c = 0,
    [Display(Name = "SNMP v3")]
    V3 = 1,
    [Display(Name = "SNMP v1 (legacy)")]
    V1 = 2
}

public enum SnmpSecurityLevel
{
    NoAuthNoPriv,
    AuthNoPriv,
    AuthPriv
}

public enum SnmpAuthenticationProtocol
{
    [Display(Name = "MD5 (legacy)")]
    Md5,
    [Display(Name = "SHA-1 (legacy)")]
    Sha1,
    Sha256,
    Sha384,
    Sha512
}

public enum SnmpPrivacyProtocol
{
    [Display(Name = "DES (legacy)")]
    Des,
    Aes128,
    Aes192,
    Aes256
}
