using System.Text.Json;
using System.Text.Json.Serialization;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.NetworkMaps;

public sealed class NetworkMapService(
    INetworkMapRepository maps,
    IDeviceRepository devices,
    IInventoryStructureRepository structure) : INetworkMapService
{
    private const double CanvasWidth = 3200;
    private const double CanvasHeight = 2000;
    private static readonly JsonSerializerOptions ImportOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<IReadOnlyList<NetworkMap>> GetAllAsync(CancellationToken cancellationToken = default) => maps.GetAllAsync(cancellationToken);

    public Task<NetworkMap?> GetAsync(Guid id, CancellationToken cancellationToken = default) => maps.GetAsync(id, cancellationToken);

    public async Task<MapMutationResult> SaveAsync(NetworkMap map, CancellationToken cancellationToken = default)
    {
        NormalizeMap(map);
        var validation = await ValidateAsync(map, cancellationToken);
        if (validation.Error is not null) return new(MapWriteResult.Invalid, validation.Error, validation.Missing);
        var result = await maps.SaveAsync(map, cancellationToken);
        return result == MapWriteResult.DuplicateName
            ? new(result, "A map with this name already exists for the selected location.")
            : new(result, MapId: result == MapWriteResult.Success ? map.Id : null);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => maps.DeleteAsync(id, cancellationToken);

    public async Task<MapImportPreview> PreviewImportAsync(string json, CancellationToken cancellationToken = default)
    {
        if (json.Length > 2_000_000) return new(null, [], "Map JSON files must be 2 MB or smaller.");
        NetworkMap? map;
        try
        {
            map = JsonSerializer.Deserialize<NetworkMap>(json, ImportOptions);
        }
        catch (JsonException exception)
        {
            return new(null, [], $"The map JSON is invalid: {exception.Message}");
        }

        if (map is null) return new(null, [], "The JSON file did not contain a network map.");
        map.Nodes ??= [];
        map.Connections ??= [];
        map.Annotations ??= [];
        NormalizeMap(map);
        var validation = await ValidateAsync(map, cancellationToken, allowMissingDevices: true);
        return new(map, validation.Missing, validation.Error);
    }

    public async Task<MapMutationResult> ImportAsync(NetworkMap map, string? newName = null, CancellationToken cancellationToken = default)
    {
        map.Id = Guid.NewGuid();
        map.CreatedUtc = DateTimeOffset.UtcNow;
        map.UpdatedUtc = map.CreatedUtc;
        map.Name = string.IsNullOrWhiteSpace(newName) ? map.Name : newName;
        NormalizeMap(map);
        foreach (var connection in map.Connections)
        {
            connection.Id = Guid.NewGuid();
        }

        foreach (var annotation in map.Annotations)
        {
            annotation.Id = Guid.NewGuid();
        }

        var validation = await ValidateAsync(map, cancellationToken);
        if (validation.Error is not null) return new(MapWriteResult.Invalid, validation.Error, validation.Missing);
        var result = await maps.SaveAsync(map, cancellationToken);
        return result == MapWriteResult.DuplicateName
            ? new(result, "A map with this name already exists for the selected location.")
            : new(result, MapId: result == MapWriteResult.Success ? map.Id : null);
    }

    private async Task<ValidationResult> ValidateAsync(
        NetworkMap map,
        CancellationToken cancellationToken,
        bool allowMissingDevices = false)
    {
        if (string.IsNullOrWhiteSpace(map.Name) || map.Name.Trim().Length > 120)
        {
            return new("Enter a map name up to 120 characters long.", []);
        }

        if (map.Nodes is null || map.Connections is null || map.Annotations is null ||
            map.Nodes.Any(node => node is null) || map.Connections.Any(connection => connection is null) || map.Annotations.Any(annotation => annotation is null))
        {
            return new("The map JSON has a missing node or connection list entry.", []);
        }

        if (map.Nodes.Count > 500 || map.Connections.Count > 1000 || map.Annotations.Count > 1000)
        {
            return new("A map can contain up to 500 items, 1,000 connections, and 1,000 drawing elements.", []);
        }

        var hierarchy = await structure.GetAsync(cancellationToken);
        var devicesById = (await devices.GetAllAsync(cancellationToken)).ToDictionary(device => device.Id);
        var groupsById = hierarchy.Groups.ToDictionary(group => group.Id);
        var locationsById = hierarchy.Locations.ToDictionary(location => location.Id);
        var missing = map.Nodes.Where(node => node.Kind == NetworkMapNodeKind.Device)
            .Select(node => node.DeviceId).Distinct().Where(id => !devicesById.ContainsKey(id)).ToArray();
        if (map.LocationId is { } locationId)
        {
            var location = hierarchy.Locations.FirstOrDefault(item => item.Id == locationId);
            if (location is null || location.Kind == LocationKind.Building)
            {
                return new("Choose an existing site or area for this map.", missing);
            }
        }

        if (map.BuildingId is { } buildingId)
        {
            var building = hierarchy.Locations.FirstOrDefault(item => item.Id == buildingId);
            if (building is null || building.Kind != LocationKind.Building ||
                (map.LocationId is { } parentId && !IsLocationDescendant(hierarchy.Locations, buildingId, parentId)))
            {
                return new("Choose a building under the selected location.", missing);
            }
        }

        if (missing.Length > 0 && !allowMissingDevices)
        {
            return new("Every device item must reference an existing device. Remove or replace missing device IDs before saving.", missing);
        }

        if (map.Nodes.Any(node => node.Id == Guid.Empty || !Enum.IsDefined(node.Kind) || !Enum.IsDefined(node.Icon) ||
            !double.IsFinite(node.X) || !double.IsFinite(node.Y) ||
            !double.IsFinite(node.Width) || !double.IsFinite(node.Height) ||
            node.X < 0 || node.X > CanvasWidth || node.Y < 0 || node.Y > CanvasHeight ||
            node.Width is < 48 or > 320 || node.Height is < 48 or > 260 ||
            (node.Kind == NetworkMapNodeKind.Device && (node.DeviceId == Guid.Empty || !devicesById.ContainsKey(node.DeviceId))) ||
            (node.Kind == NetworkMapNodeKind.Group && (node.GroupId is not { } groupId || !groupsById.ContainsKey(groupId))) ||
            (node.Kind == NetworkMapNodeKind.Location && (node.LocationId is not { } locationId || !locationsById.ContainsKey(locationId)))))
        {
            return new("A map item has an invalid inventory reference, icon style, position, or size.", missing);
        }

        if (map.Nodes.Select(node => node.Id).Distinct().Count() != map.Nodes.Count)
        {
            return new("Map item IDs must be unique.", missing);
        }

        if (map.Nodes.GroupBy(NodeReferenceKey).Any(group => group.Count() > 1))
        {
            return new("An inventory device, group, or site can appear only once on each map.", missing);
        }

        if (map.Connections.Any(connection => connection.Id == Guid.Empty ||
            ResolveNode(connection.FromNodeId, connection.FromDeviceId, map.Nodes) is not { } from ||
            ResolveNode(connection.ToNodeId, connection.ToDeviceId, map.Nodes) is not { } to || from.Id == to.Id ||
            !Enum.IsDefined(connection.Kind) || !double.IsFinite(connection.StrokeWidth) || connection.StrokeWidth is < 1 or > 12 || connection.Label?.Length > 120))
        {
            return new("Every connection must link two different map items and use a supported type and line width.", missing);
        }

        if (map.Connections.Select(connection => PairKey(
                ResolveNode(connection.FromNodeId, connection.FromDeviceId, map.Nodes)!.Id,
                ResolveNode(connection.ToNodeId, connection.ToDeviceId, map.Nodes)!.Id))
            .Distinct().Count() != map.Connections.Count)
        {
            return new("Duplicate connections between the same map items are not allowed.", missing);
        }

        if (map.Connections.Select(connection => connection.Id).Distinct().Count() != map.Connections.Count)
        {
            return new("Connection IDs must be unique within a map.", missing);
        }

        if (map.Annotations.Any(annotation => annotation.Id == Guid.Empty || !Enum.IsDefined(annotation.Kind) ||
            !double.IsFinite(annotation.X) || !double.IsFinite(annotation.Y) ||
            !double.IsFinite(annotation.Width) || !double.IsFinite(annotation.Height) ||
            annotation.X < 0 || annotation.X > CanvasWidth || annotation.Y < 0 || annotation.Y > CanvasHeight ||
            Math.Abs(annotation.Width) > CanvasWidth || Math.Abs(annotation.Height) > CanvasHeight ||
            !IsHexColor(annotation.StrokeColor) || (annotation.FillColor is not null && !IsHexColor(annotation.FillColor)) ||
            !double.IsFinite(annotation.StrokeWidth) || annotation.StrokeWidth is < 1 or > 12 ||
            annotation.Text?.Length > 240 || (annotation.Kind == NetworkMapAnnotationKind.Text && string.IsNullOrWhiteSpace(annotation.Text))))
        {
            return new("A drawing element has an invalid type, position, color, size, or text.", missing);
        }

        if (map.Annotations.Select(annotation => annotation.Id).Distinct().Count() != map.Annotations.Count)
        {
            return new("Drawing element IDs must be unique within a map.", missing);
        }

        return new(null, missing);
    }

    private static void NormalizeMap(NetworkMap map)
    {
        map.Nodes ??= [];
        map.Connections ??= [];
        map.Annotations ??= [];
        foreach (var node in map.Nodes.Where(node => node is not null))
        {
            if (node.Id == Guid.Empty)
            {
                node.Id = node.Kind == NetworkMapNodeKind.Device && node.DeviceId != Guid.Empty ? node.DeviceId : Guid.NewGuid();
            }
        }

        foreach (var connection in map.Connections.Where(connection => connection is not null))
        {
            connection.FromNodeId ??= map.Nodes.FirstOrDefault(node => node is not null && node.Kind == NetworkMapNodeKind.Device && node.DeviceId == connection.FromDeviceId)?.Id;
            connection.ToNodeId ??= map.Nodes.FirstOrDefault(node => node is not null && node.Kind == NetworkMapNodeKind.Device && node.DeviceId == connection.ToDeviceId)?.Id;
        }
    }

    private static NetworkMapNode? ResolveNode(Guid? nodeId, Guid deviceId, IReadOnlyCollection<NetworkMapNode> nodes) =>
        nodeId is { } id && id != Guid.Empty
            ? nodes.FirstOrDefault(node => node.Id == id)
            : nodes.FirstOrDefault(node => node.Kind == NetworkMapNodeKind.Device && node.DeviceId == deviceId);

    private static string NodeReferenceKey(NetworkMapNode node) => node.Kind switch
    {
        NetworkMapNodeKind.Device => $"Device:{node.DeviceId:N}",
        NetworkMapNodeKind.Group => $"Group:{node.GroupId:N}",
        NetworkMapNodeKind.Location => $"Location:{node.LocationId:N}",
        _ => $"Unknown:{node.Id:N}"
    };

    private static bool IsHexColor(string value) =>
        value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);

    private static string PairKey(Guid first, Guid second) => first.CompareTo(second) <= 0 ? $"{first:N}:{second:N}" : $"{second:N}:{first:N}";

    private static bool IsLocationDescendant(IReadOnlyCollection<ManagedLocation> locations, Guid childId, Guid ancestorId)
    {
        var byId = locations.ToDictionary(item => item.Id);
        var current = childId;
        var visited = new HashSet<Guid>();
        while (visited.Add(current) && byId.TryGetValue(current, out var node) && node.ParentLocationId is { } parent)
        {
            if (parent == ancestorId) return true;
            current = parent;
        }

        return false;
    }

    private sealed record ValidationResult(string? Error, IReadOnlyList<Guid> Missing);
}
