using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class NetworkMapsIndexViewModel
{
    public IReadOnlyList<NetworkMap> Maps { get; init; } = [];
}

public sealed class NetworkMapEditorViewModel
{
    public Guid? MapId { get; init; }
    public IReadOnlyList<NetworkMap> Maps { get; init; } = [];
}

public sealed class NetworkMapEditorData
{
    public NetworkMap Map { get; init; } = new();
    public IReadOnlyList<MapDeviceData> Devices { get; init; } = [];
    public IReadOnlyList<HierarchyChoice> Locations { get; init; } = [];
    public IReadOnlyList<HierarchyChoice> Buildings { get; init; } = [];
    public IReadOnlyList<MapGroupChoice> Groups { get; init; } = [];
    public IReadOnlyList<MapLocationChoice> MapLocations { get; init; } = [];
}

public sealed record MapGroupChoice(Guid Id, string Name, Guid? ParentGroupId);

public sealed record MapLocationChoice(Guid Id, string Name, string Kind, Guid? ParentLocationId);

public sealed class MapDeviceData
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public string DeviceType { get; init; } = string.Empty;
    public string Status { get; init; } = "Unknown";
    public long? ResponseTimeMilliseconds { get; init; }
    public DateTimeOffset? LastCheckUtc { get; init; }
    public DateTimeOffset? CurrentStateSinceUtc { get; init; }
    public bool MonitoringEnabled { get; init; }
    public string? Location { get; init; }
    public string? Building { get; init; }
    public string? Group { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? BuildingId { get; init; }
    public Guid? GroupId { get; init; }
    public string? DetailsUrl { get; init; }
    public string? LocationUrl { get; init; }
    public string? GroupUrl { get; init; }
}

public sealed class MapDeviceStatusData
{
    public Guid Id { get; init; }
    public string Status { get; init; } = "Unknown";
    public long? ResponseTimeMilliseconds { get; init; }
    public DateTimeOffset? LastCheckUtc { get; init; }
    public DateTimeOffset? CurrentStateSinceUtc { get; init; }
    public bool MonitoringEnabled { get; init; }
}

public sealed record MapSaveResponse(bool Success, Guid? MapId = null, string? Error = null, IReadOnlyList<Guid>? MissingDeviceIds = null);

public sealed record MapImportRequest(string Json, string? Name = null);

public sealed record MapImportResponse(bool Success, bool Preview, string? Error = null, IReadOnlyList<Guid>? MissingDeviceIds = null, Guid? MapId = null, string? MapName = null);
