using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Storage.Json;

public sealed class JsonInventoryStructureRepository(IJsonDocumentStore documents) : IInventoryStructureRepository
{
    private const string DocumentName = "inventory-structure.json";

    public Task<InventoryStructureDocument> GetAsync(CancellationToken cancellationToken = default) =>
        documents.ReadAsync(DocumentName, static () => new InventoryStructureDocument(), cancellationToken);

    public Task<StructureWriteResult> CreateLocationAsync(ManagedLocation location, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<InventoryStructureDocument, StructureWriteResult>(
            DocumentName,
            static () => new InventoryStructureDocument(),
            document =>
            {
                if (string.IsNullOrWhiteSpace(location.Name) || location.Name.Trim().Length > 120)
                {
                    return (document, StructureWriteResult.InvalidName);
                }

                if (!ValidParent(document.Locations, location.ParentLocationId) ||
                    location.ParentLocationId == location.Id ||
                    !Enum.IsDefined(location.Kind))
                {
                    return (document, StructureWriteResult.InvalidParent);
                }

                location.Name = location.Name.Trim();
                if (HasSibling(document.Locations, location.Name, location.ParentLocationId, null, item => item.Name, item => item.ParentLocationId))
                {
                    return (document, StructureWriteResult.DuplicateName);
                }

                location.CreatedUtc = DateTimeOffset.UtcNow;
                location.UpdatedUtc = location.CreatedUtc;
                document.Locations.Add(location);
                return (document, StructureWriteResult.Success);
            },
            cancellationToken);

    public Task<StructureWriteResult> UpdateLocationAsync(ManagedLocation location, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<InventoryStructureDocument, StructureWriteResult>(
            DocumentName,
            static () => new InventoryStructureDocument(),
            document =>
            {
                var existing = document.Locations.FirstOrDefault(item => item.Id == location.Id);
                if (existing is null)
                {
                    return (document, StructureWriteResult.NotFound);
                }

                if (string.IsNullOrWhiteSpace(location.Name) || location.Name.Trim().Length > 120)
                {
                    return (document, StructureWriteResult.InvalidName);
                }

                if (!ValidParent(document.Locations, location.ParentLocationId) ||
                    location.ParentLocationId == location.Id ||
                    IsDescendant(document.Locations, location.ParentLocationId, location.Id) ||
                    !Enum.IsDefined(location.Kind))
                {
                    return (document, StructureWriteResult.InvalidParent);
                }

                location.Name = location.Name.Trim();
                if (HasSibling(document.Locations, location.Name, location.ParentLocationId, location.Id, item => item.Name, item => item.ParentLocationId))
                {
                    return (document, StructureWriteResult.DuplicateName);
                }

                existing.Name = location.Name;
                existing.Kind = location.Kind;
                existing.ParentLocationId = location.ParentLocationId;
                existing.UpdatedUtc = DateTimeOffset.UtcNow;
                return (document, StructureWriteResult.Success);
            },
            cancellationToken);

    public Task<StructureWriteResult> DeleteLocationAsync(Guid id, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<InventoryStructureDocument, StructureWriteResult>(
            DocumentName,
            static () => new InventoryStructureDocument(),
            document =>
            {
                if (!document.Locations.Any(item => item.Id == id))
                {
                    return (document, StructureWriteResult.NotFound);
                }

                if (document.Locations.Any(item => item.ParentLocationId == id))
                {
                    return (document, StructureWriteResult.HasChildren);
                }

                document.Locations.RemoveAll(item => item.Id == id);
                return (document, StructureWriteResult.Success);
            },
            cancellationToken);

    public Task<StructureWriteResult> CreateGroupAsync(ManagedGroup group, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<InventoryStructureDocument, StructureWriteResult>(
            DocumentName,
            static () => new InventoryStructureDocument(),
            document =>
            {
                if (string.IsNullOrWhiteSpace(group.Name) || group.Name.Trim().Length > 120)
                {
                    return (document, StructureWriteResult.InvalidName);
                }

                if (!ValidParent(document.Groups, group.ParentGroupId) || group.ParentGroupId == group.Id)
                {
                    return (document, StructureWriteResult.InvalidParent);
                }

                group.Name = group.Name.Trim();
                if (HasSibling(document.Groups, group.Name, group.ParentGroupId, null, item => item.Name, item => item.ParentGroupId))
                {
                    return (document, StructureWriteResult.DuplicateName);
                }

                group.CreatedUtc = DateTimeOffset.UtcNow;
                group.UpdatedUtc = group.CreatedUtc;
                document.Groups.Add(group);
                return (document, StructureWriteResult.Success);
            },
            cancellationToken);

    public Task<StructureWriteResult> UpdateGroupAsync(ManagedGroup group, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<InventoryStructureDocument, StructureWriteResult>(
            DocumentName,
            static () => new InventoryStructureDocument(),
            document =>
            {
                var existing = document.Groups.FirstOrDefault(item => item.Id == group.Id);
                if (existing is null)
                {
                    return (document, StructureWriteResult.NotFound);
                }

                if (string.IsNullOrWhiteSpace(group.Name) || group.Name.Trim().Length > 120)
                {
                    return (document, StructureWriteResult.InvalidName);
                }

                if (!ValidParent(document.Groups, group.ParentGroupId) ||
                    group.ParentGroupId == group.Id ||
                    IsDescendant(document.Groups, group.ParentGroupId, group.Id))
                {
                    return (document, StructureWriteResult.InvalidParent);
                }

                group.Name = group.Name.Trim();
                if (HasSibling(document.Groups, group.Name, group.ParentGroupId, group.Id, item => item.Name, item => item.ParentGroupId))
                {
                    return (document, StructureWriteResult.DuplicateName);
                }

                existing.Name = group.Name;
                existing.ParentGroupId = group.ParentGroupId;
                existing.UpdatedUtc = DateTimeOffset.UtcNow;
                return (document, StructureWriteResult.Success);
            },
            cancellationToken);

    public Task<StructureWriteResult> DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<InventoryStructureDocument, StructureWriteResult>(
            DocumentName,
            static () => new InventoryStructureDocument(),
            document =>
            {
                if (!document.Groups.Any(item => item.Id == id))
                {
                    return (document, StructureWriteResult.NotFound);
                }

                if (document.Groups.Any(item => item.ParentGroupId == id))
                {
                    return (document, StructureWriteResult.HasChildren);
                }

                document.Groups.RemoveAll(item => item.Id == id);
                return (document, StructureWriteResult.Success);
            },
            cancellationToken);

    public Task<ResolvedDeviceAssignment> ResolveOrCreateLegacyAssignmentAsync(
        string? location,
        string? building,
        string? group,
        CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<InventoryStructureDocument, ResolvedDeviceAssignment>(
            DocumentName,
            static () => new InventoryStructureDocument(),
            document =>
            {
                Guid? locationId = null;
                Guid? buildingId = null;
                Guid? groupId = null;
                var timestamp = DateTimeOffset.UtcNow;
                var locationName = TrimOrNull(location);
                var buildingName = TrimOrNull(building);
                var groupName = TrimOrNull(group);

                if (locationName is not null)
                {
                    var site = document.Locations.FirstOrDefault(item => item.Kind == LocationKind.Site &&
                        item.ParentLocationId is null && string.Equals(item.Name, locationName, StringComparison.OrdinalIgnoreCase));
                    if (site is null)
                    {
                        site = new ManagedLocation { Name = locationName, Kind = LocationKind.Site, CreatedUtc = timestamp, UpdatedUtc = timestamp };
                        document.Locations.Add(site);
                    }

                    locationId = site.Id;
                }

                if (buildingName is not null)
                {
                    var buildingNode = document.Locations.FirstOrDefault(item => item.Kind == LocationKind.Building &&
                        item.ParentLocationId == locationId && string.Equals(item.Name, buildingName, StringComparison.OrdinalIgnoreCase));
                    if (buildingNode is null)
                    {
                        buildingNode = new ManagedLocation
                        {
                            Name = buildingName,
                            Kind = LocationKind.Building,
                            ParentLocationId = locationId,
                            CreatedUtc = timestamp,
                            UpdatedUtc = timestamp
                        };
                        document.Locations.Add(buildingNode);
                    }

                    buildingId = buildingNode.Id;
                }

                if (groupName is not null)
                {
                    var groupNode = document.Groups.FirstOrDefault(item => item.ParentGroupId is null &&
                        string.Equals(item.Name, groupName, StringComparison.OrdinalIgnoreCase));
                    if (groupNode is null)
                    {
                        groupNode = new ManagedGroup { Name = groupName, CreatedUtc = timestamp, UpdatedUtc = timestamp };
                        document.Groups.Add(groupNode);
                    }

                    groupId = groupNode.Id;
                }

                return (document, new ResolvedDeviceAssignment(locationId, buildingId, groupId));
            },
            cancellationToken);

    private static bool ValidParent<T>(IReadOnlyCollection<T> nodes, Guid? parentId) where T : class =>
        parentId is null || nodes.Any(node => GetId(node) == parentId);

    private static bool IsDescendant<T>(IReadOnlyCollection<T> nodes, Guid? possibleDescendant, Guid ancestorId) where T : class
    {
        var parents = nodes.ToDictionary(GetId, GetParentId);
        var current = possibleDescendant;
        var visited = new HashSet<Guid>();
        while (current is { } id && visited.Add(id) && parents.TryGetValue(id, out current))
        {
            if (current == ancestorId)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSibling<T>(
        IEnumerable<T> nodes,
        string name,
        Guid? parentId,
        Guid? excludingId,
        Func<T, string> getName,
        Func<T, Guid?> getParent) where T : class =>
        nodes.Any(node => GetId(node) != excludingId && getParent(node) == parentId &&
                          string.Equals(getName(node), name, StringComparison.OrdinalIgnoreCase));

    private static Guid GetId<T>(T node) where T : class => node switch
    {
        ManagedLocation location => location.Id,
        ManagedGroup group => group.Id,
        _ => Guid.Empty
    };

    private static Guid? GetParentId<T>(T node) where T : class => node switch
    {
        ManagedLocation location => location.ParentLocationId,
        ManagedGroup group => group.ParentGroupId,
        _ => null
    };

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
