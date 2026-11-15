using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Ipam;
using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class IpamIndexViewModel
{
    public IReadOnlyList<IpamRecordRow> Records { get; init; } = [];
    public IReadOnlyList<IpamSubnet> Subnets { get; init; } = [];
    public IReadOnlyList<IpamSubnet> MatchedSubnets { get; init; } = [];
    public string? Search { get; init; }
    public IpAssignmentStatus? Status { get; init; }
}

public sealed class IpamRecordRow
{
    public IpamRecord Record { get; init; } = new();
    public DeviceStatus? CurrentStatus { get; init; }
    public long? ResponseTimeMilliseconds { get; init; }
    public DateTimeOffset? LastCheckUtc { get; init; }
}

public sealed class IpamRecordEditViewModel
{
    public IpamRecord Record { get; set; } = new() { Id = Guid.Empty };
    public IReadOnlyList<DeviceInfo> Devices { get; set; } = [];
    public IReadOnlyList<IpamSubnet> Subnets { get; set; } = [];
    public IReadOnlyList<HierarchyChoice> Locations { get; set; } = [];
    public IReadOnlyList<HierarchyChoice> Buildings { get; set; } = [];
    public IReadOnlyList<HierarchyChoice> Groups { get; set; } = [];
}

public sealed class IpamSubnetsViewModel
{
    public IReadOnlyList<IpamSubnet> Subnets { get; init; } = [];
    public IReadOnlyList<IpamRecord> Records { get; init; } = [];
    public IReadOnlyList<HierarchyChoice> Locations { get; init; } = [];
    public IReadOnlyList<HierarchyChoice> Buildings { get; init; } = [];
    public IpamSubnet NewSubnet { get; set; } = new() { Id = Guid.Empty };
    public string? Error { get; set; }
}

public sealed class IpamSubnetEditViewModel
{
    public IpamSubnet Subnet { get; set; } = new() { Id = Guid.Empty };
    public IReadOnlyList<HierarchyChoice> Locations { get; set; } = [];
    public IReadOnlyList<HierarchyChoice> Buildings { get; set; } = [];
}

public sealed record IpamCsvRequest(string Csv, Dictionary<string, string?> Mapping);

public sealed class IpamImportViewModel
{
    public IReadOnlyList<IpamCsvPreviewRow> Rows { get; init; } = [];
    public IReadOnlyList<string> Headers { get; init; } = [];
    public string? Error { get; init; }
    public int ValidCount { get; init; }
    public int InvalidCount { get; init; }
    public int DuplicateCount { get; init; }
}
