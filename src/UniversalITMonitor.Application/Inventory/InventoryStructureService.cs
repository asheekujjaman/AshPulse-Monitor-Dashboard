using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Inventory;

public sealed class InventoryStructureService(
    IInventoryStructureRepository structure,
    IDeviceRepository devices) : IInventoryStructureService
{
    public Task<InventoryStructureDocument> GetStructureAsync(CancellationToken cancellationToken = default) =>
        structure.GetAsync(cancellationToken);

    public async Task<IReadOnlyList<LocationSummary>> GetLocationSummariesAsync(CancellationToken cancellationToken = default)
    {
        var hierarchy = await structure.GetAsync(cancellationToken);
        var allDevices = await devices.GetAllAsync(cancellationToken);
        return hierarchy.Locations
            .OrderBy(location => BuildLocationPath(hierarchy.Locations, location.Id), StringComparer.OrdinalIgnoreCase)
            .Select(location =>
            {
                var descendants = DescendantLocationIds(hierarchy.Locations, location.Id);
                var matching = allDevices.Where(device =>
                    (device.LocationId is { } locationId && descendants.Contains(locationId)) ||
                    (device.BuildingId is { } buildingId && descendants.Contains(buildingId))).ToArray();
                return new LocationSummary(
                    location,
                    BuildLocationPath(hierarchy.Locations, location.Id),
                    matching.Length,
                    matching.Count(device => device.Status == DeviceStatus.Up),
                    matching.Count(device => device.Status == DeviceStatus.Down),
                    matching.Count(device => device.Status == DeviceStatus.Unknown),
                    matching.Select(device => device.Id).ToArray());
            }).ToArray();
    }

    public async Task<IReadOnlyList<GroupSummary>> GetGroupSummariesAsync(CancellationToken cancellationToken = default)
    {
        var hierarchy = await structure.GetAsync(cancellationToken);
        var allDevices = await devices.GetAllAsync(cancellationToken);
        return hierarchy.Groups
            .OrderBy(group => BuildGroupPath(hierarchy.Groups, group.Id), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var descendants = DescendantGroupIds(hierarchy.Groups, group.Id);
                var matching = allDevices.Where(device => device.GroupId is { } groupId && descendants.Contains(groupId)).ToArray();
                return new GroupSummary(
                    group,
                    BuildGroupPath(hierarchy.Groups, group.Id),
                    matching.Length,
                    matching.Count(device => device.Status == DeviceStatus.Up),
                    matching.Count(device => device.Status == DeviceStatus.Down),
                    matching.Count(device => device.Status == DeviceStatus.Unknown),
                    matching.Select(device => device.Id).ToArray());
            }).ToArray();
    }

    public async Task<string?> ValidateDeviceAssignmentAsync(
        Guid? locationId,
        Guid? buildingId,
        Guid? groupId,
        CancellationToken cancellationToken = default)
    {
        var hierarchy = await structure.GetAsync(cancellationToken);
        if (locationId is { } locationKey)
        {
            var location = hierarchy.Locations.FirstOrDefault(item => item.Id == locationKey);
            if (location is null || location.Kind == LocationKind.Building)
            {
                return "Choose an existing location or area.";
            }
        }

        if (buildingId is { } buildingKey)
        {
            var building = hierarchy.Locations.FirstOrDefault(item => item.Id == buildingKey);
            if (building is null || building.Kind != LocationKind.Building ||
                (locationId is { } parentId && !IsDescendant(hierarchy.Locations, buildingKey, parentId)))
            {
                return "Choose a building under the selected location.";
            }
        }

        if (groupId is { } groupKey && !hierarchy.Groups.Any(item => item.Id == groupKey))
        {
            return "Choose an existing group.";
        }

        return null;
    }

    public Task<StructureWriteResult> CreateLocationAsync(ManagedLocation location, CancellationToken cancellationToken = default)
    {
        if (!ValidName(location.Name)) return Task.FromResult(StructureWriteResult.InvalidName);
        return structure.CreateLocationAsync(location, cancellationToken);
    }

    public Task<StructureWriteResult> UpdateLocationAsync(ManagedLocation location, CancellationToken cancellationToken = default)
    {
        if (!ValidName(location.Name)) return Task.FromResult(StructureWriteResult.InvalidName);
        return structure.UpdateLocationAsync(location, cancellationToken);
    }

    public async Task<HierarchyMutationResult> DeleteLocationAsync(
        Guid id,
        Guid? reassignToLocationId,
        bool confirmUnassignDevices,
        CancellationToken cancellationToken = default)
    {
        var hierarchy = await structure.GetAsync(cancellationToken);
        var location = hierarchy.Locations.FirstOrDefault(item => item.Id == id);
        if (location is null) return new(StructureWriteResult.NotFound);
        if (hierarchy.Locations.Any(item => item.ParentLocationId == id)) return new(StructureWriteResult.HasChildren);

        var reassignment = reassignToLocationId is { } targetId
            ? hierarchy.Locations.FirstOrDefault(item => item.Id == targetId)
            : null;
        if (reassignToLocationId is not null &&
            (reassignment is null || reassignment.Id == id || IsDescendant(hierarchy.Locations, reassignment.Id, id)))
        {
            return new(StructureWriteResult.InvalidParent, Error: "Choose a different existing location for reassignment.");
        }

        var allDevices = await devices.GetAllAsync(cancellationToken);
        var affected = allDevices.Where(device => device.LocationId == id || device.BuildingId == id).ToArray();
        if (affected.Length > 0 && reassignment is null && !confirmUnassignDevices)
        {
            return new(StructureWriteResult.InUse, affected.Length, "Reassign the devices or confirm that their location assignment should be cleared.");
        }

        if (affected.Length > 0)
        {
            var updates = affected.Select(device => BuildLocationReassignment(device, id, reassignment, hierarchy.Locations)).ToArray();
            await devices.UpdateAssignmentsAsync(updates, cancellationToken);
        }

        var result = await structure.DeleteLocationAsync(id, cancellationToken);
        return new(result, affected.Length);
    }

    public Task<StructureWriteResult> CreateGroupAsync(ManagedGroup group, CancellationToken cancellationToken = default)
    {
        if (!ValidName(group.Name)) return Task.FromResult(StructureWriteResult.InvalidName);
        return structure.CreateGroupAsync(group, cancellationToken);
    }

    public Task<StructureWriteResult> UpdateGroupAsync(ManagedGroup group, CancellationToken cancellationToken = default)
    {
        if (!ValidName(group.Name)) return Task.FromResult(StructureWriteResult.InvalidName);
        return structure.UpdateGroupAsync(group, cancellationToken);
    }

    public async Task<HierarchyMutationResult> DeleteGroupAsync(
        Guid id,
        Guid? reassignToGroupId,
        bool confirmUnassignDevices,
        CancellationToken cancellationToken = default)
    {
        var hierarchy = await structure.GetAsync(cancellationToken);
        if (!hierarchy.Groups.Any(item => item.Id == id)) return new(StructureWriteResult.NotFound);
        if (hierarchy.Groups.Any(item => item.ParentGroupId == id)) return new(StructureWriteResult.HasChildren);

        var reassignment = reassignToGroupId is { } targetId
            ? hierarchy.Groups.FirstOrDefault(item => item.Id == targetId)
            : null;
        if (reassignToGroupId is not null &&
            (reassignment is null || reassignment.Id == id || IsDescendant(hierarchy.Groups, reassignment.Id, id)))
        {
            return new(StructureWriteResult.InvalidParent, Error: "Choose a different existing group for reassignment.");
        }

        var allDevices = await devices.GetAllAsync(cancellationToken);
        var affected = allDevices.Where(device => device.GroupId == id).ToArray();
        if (affected.Length > 0 && reassignment is null && !confirmUnassignDevices)
        {
            return new(StructureWriteResult.InUse, affected.Length, "Reassign the devices or confirm that their group assignment should be cleared.");
        }

        if (affected.Length > 0)
        {
            var updates = affected.Select(device => new DeviceAssignmentUpdate(
                device.Id,
                device.LocationId,
                device.BuildingId,
                reassignment?.Id,
                device.Location,
                device.Building,
                reassignment?.Name)).ToArray();
            await devices.UpdateAssignmentsAsync(updates, cancellationToken);
        }

        var result = await structure.DeleteGroupAsync(id, cancellationToken);
        return new(result, affected.Length);
    }

    public static string BuildLocationPath(IReadOnlyCollection<ManagedLocation> locations, Guid id)
    {
        var byId = locations.ToDictionary(item => item.Id);
        var parts = new List<string>();
        var current = id;
        var seen = new HashSet<Guid>();
        while (seen.Add(current) && byId.TryGetValue(current, out var node))
        {
            parts.Add(node.Name);
            if (node.ParentLocationId is not { } parentId) break;
            current = parentId;
        }

        parts.Reverse();
        return string.Join(" / ", parts);
    }

    public static string BuildGroupPath(IReadOnlyCollection<ManagedGroup> groups, Guid id)
    {
        var byId = groups.ToDictionary(item => item.Id);
        var parts = new List<string>();
        var current = id;
        var seen = new HashSet<Guid>();
        while (seen.Add(current) && byId.TryGetValue(current, out var node))
        {
            parts.Add(node.Name);
            if (node.ParentGroupId is not { } parentId) break;
            current = parentId;
        }

        parts.Reverse();
        return string.Join(" / ", parts);
    }

    public static IReadOnlySet<Guid> DescendantLocationIds(IReadOnlyCollection<ManagedLocation> locations, Guid id)
    {
        var result = new HashSet<Guid> { id };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var child in locations)
            {
                if (child.ParentLocationId is { } parent && result.Contains(parent) && result.Add(child.Id)) changed = true;
            }
        }

        return result;
    }

    public static IReadOnlySet<Guid> DescendantGroupIds(IReadOnlyCollection<ManagedGroup> groups, Guid id)
    {
        var result = new HashSet<Guid> { id };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var child in groups)
            {
                if (child.ParentGroupId is { } parent && result.Contains(parent) && result.Add(child.Id)) changed = true;
            }
        }

        return result;
    }

    private static DeviceAssignmentUpdate BuildLocationReassignment(
        NetworkDevice device,
        Guid removedId,
        ManagedLocation? target,
        IReadOnlyCollection<ManagedLocation> locations)
    {
        if (target is null)
        {
            return new DeviceAssignmentUpdate(
                device.Id,
                device.LocationId == removedId ? null : device.LocationId,
                device.BuildingId == removedId || device.LocationId == removedId ? null : device.BuildingId,
                device.GroupId,
                device.LocationId == removedId ? null : device.Location,
                device.BuildingId == removedId || device.LocationId == removedId ? null : device.Building,
                device.Group);
        }

        var locationId = target.Kind == LocationKind.Building ? target.ParentLocationId : target.Id;
        Guid? buildingId = target.Kind == LocationKind.Building ? target.Id : null;
        var locationName = locationId is { } parentId
            ? locations.FirstOrDefault(item => item.Id == parentId)?.Name
            : null;
        return new DeviceAssignmentUpdate(device.Id, locationId, buildingId, device.GroupId, locationName, target.Kind == LocationKind.Building ? target.Name : null, device.Group);
    }

    private static bool IsDescendant(IReadOnlyCollection<ManagedLocation> locations, Guid childId, Guid ancestorId)
    {
        var byId = locations.ToDictionary(item => item.Id);
        var current = childId;
        var seen = new HashSet<Guid>();
        while (seen.Add(current) && byId.TryGetValue(current, out var node) && node.ParentLocationId is { } parent)
        {
            if (parent == ancestorId) return true;
            current = parent;
        }

        return false;
    }

    private static bool IsDescendant(IReadOnlyCollection<ManagedGroup> groups, Guid childId, Guid ancestorId)
    {
        var byId = groups.ToDictionary(item => item.Id);
        var current = childId;
        var seen = new HashSet<Guid>();
        while (seen.Add(current) && byId.TryGetValue(current, out var node) && node.ParentGroupId is { } parent)
        {
            if (parent == ancestorId) return true;
            current = parent;
        }

        return false;
    }

    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 120;
}
