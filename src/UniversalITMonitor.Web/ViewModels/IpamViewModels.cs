using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Ipam;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Monitoring;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class IpamIndexViewModel
{
    public IReadOnlyList<IpamRecordRow> Records { get; init; } = [];
    public IReadOnlyList<IpamSubnet> Subnets { get; init; } = [];
    public IReadOnlyList<IpamSubnet> MatchedSubnets { get; init; } = [];
    public string? Search { get; init; }
    public IpAssignmentStatus? Status { get; init; }
    public int TotalAssigned { get; init; }
    public int TotalReserved { get; init; }
    public int TotalConflict { get; init; }
    public int TotalUnknown { get; init; }
    public IReadOnlyList<IpamSubnetSummaryRow> SubnetSummaries { get; init; } = [];
}

public sealed record IpamSubnetSummaryRow(IpamSubnet Subnet, long UsableCount, long AssignedCount, long ReservedCount, long AvailableCount, long ConflictCount, long UnknownCount);

public sealed class IpamExplorerViewModel
{
    public required IpamSubnet Subnet { get; init; }
    public IReadOnlyList<IpamExplorerRow> Rows { get; init; } = [];
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 100;
    public long Total { get; init; }
    public long Assigned { get; init; }
    public long Reserved { get; init; }
    public long Available { get; init; }
    public long Conflict { get; init; }
    public long Unknown { get; init; }
    public string View { get; init; } = "grid";
    public string? Search { get; init; }
    public IpAssignmentStatus? Status { get; init; }
    public bool IsIpv6 { get; init; }
    public IReadOnlyDictionary<Guid, DeviceStatus> MonitoringStatuses { get; init; } = new Dictionary<Guid, DeviceStatus>();
}

public sealed record IpamExplorerRow(string IpAddress, IpAssignmentStatus Status, IpamRecord? Record, DeviceStatus? MonitoringStatus = null);

public sealed class IpamHistoryViewModel
{
    public IReadOnlyList<IpamHistoryEvent> Events { get; init; } = [];
    public string? Search { get; init; }
    public string? Action { get; init; }
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
