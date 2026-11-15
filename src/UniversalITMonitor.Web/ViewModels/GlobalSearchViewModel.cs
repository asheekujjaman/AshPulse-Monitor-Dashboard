using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class GlobalSearchViewModel
{
    public string Query { get; init; } = string.Empty;
    public IReadOnlyList<DeviceInfo> Devices { get; init; } = [];
    public IReadOnlyList<IpamSearchResult> IpRecords { get; init; } = [];
    public IReadOnlyList<IpamSubnet> Subnets { get; init; } = [];
    public IReadOnlyList<LocationSummary> Locations { get; init; } = [];
    public IReadOnlyList<GroupSummary> Groups { get; init; } = [];
    public IReadOnlyList<NetworkMap> Maps { get; init; } = [];
}

public sealed class IpamSearchResult
{
    public IpamRecord Record { get; init; } = new();
    public DeviceStatus? CurrentStatus { get; init; }
}
