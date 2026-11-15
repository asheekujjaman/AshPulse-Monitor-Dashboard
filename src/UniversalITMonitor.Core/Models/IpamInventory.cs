namespace UniversalITMonitor.Core.Models;

public enum IpAssignmentStatus
{
    Assigned,
    Available,
    Reserved,
    Conflict,
    Unknown
}

public sealed class IpamRecord
{
    public Guid Id { get; set; }

    public Guid? DeviceId { get; set; }

    public string IpAddress { get; set; } = string.Empty;

    public Guid? SubnetId { get; set; }

    public int? VlanId { get; set; }

    public string? VlanName { get; set; }

    public string? DeviceName { get; set; }

    public string? DeviceType { get; set; }

    public Guid? LocationId { get; set; }

    public Guid? BuildingId { get; set; }

    public Guid? GroupId { get; set; }

    public string? Location { get; set; }

    public string? Building { get; set; }

    public string? Group { get; set; }

    public string? SwitchName { get; set; }

    public string? SwitchIpAddress { get; set; }

    public string? SwitchPort { get; set; }

    public string? MacAddress { get; set; }

    public string? AssignedTo { get; set; }

    public string? Department { get; set; }

    public string? Description { get; set; }

    public IpAssignmentStatus AssignmentStatus { get; set; } = IpAssignmentStatus.Unknown;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class IpamSubnet
{
    public Guid Id { get; set; }

    public string Cidr { get; set; } = string.Empty;

    public string NetworkAddress { get; set; } = string.Empty;

    public int PrefixLength { get; set; }

    public int? VlanId { get; set; }

    public string? VlanName { get; set; }

    public Guid? LocationId { get; set; }

    public Guid? BuildingId { get; set; }

    public string? Location { get; set; }

    public string? Building { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class IpamDocument
{
    public List<IpamRecord> Records { get; set; } = [];

    public List<IpamSubnet> Subnets { get; set; } = [];
}
