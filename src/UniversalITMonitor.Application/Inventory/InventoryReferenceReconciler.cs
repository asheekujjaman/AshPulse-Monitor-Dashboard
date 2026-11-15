using Microsoft.Extensions.Logging;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Inventory;

public sealed class InventoryReferenceReconciler(
    IJsonDocumentStore documents,
    ILogger<InventoryReferenceReconciler> logger)
{
    public async Task<InventoryReferenceReportDocument> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var devicesTask = documents.ReadAsync("devices.json", static () => new DeviceDocument(), cancellationToken);
        var structureTask = documents.ReadAsync("inventory-structure.json", static () => new InventoryStructureDocument(), cancellationToken);
        var ipamTask = documents.ReadAsync("ipam.json", static () => new IpamDocument(), cancellationToken);
        var mapsTask = documents.ReadAsync("network-maps.json", static () => new NetworkMapDocument(), cancellationToken);
        await Task.WhenAll(devicesTask, structureTask, ipamTask, mapsTask);

        var deviceDocument = await devicesTask;
        var structure = await structureTask;
        var ipam = await ipamTask;
        var mapDocument = await mapsTask;
        var report = new InventoryReferenceReportDocument { LastRunUtc = DateTimeOffset.UtcNow };
        var changedStructure = NormalizeDuplicateIds(structure.Locations, item => item.Id, (item, id) => item.Id = id, "Location", report);
        changedStructure |= NormalizeDuplicateIds(structure.Groups, item => item.Id, (item, id) => item.Id = id, "Group", report);
        var changedDevices = NormalizeDuplicateIds(deviceDocument.Devices, item => item.Id, (item, id) => item.Id = id, "Device", report);
        var changedIpam = NormalizeDuplicateIds(ipam.Records, item => item.Id, (item, id) => item.Id = id, "IP record", report);
        changedIpam |= NormalizeDuplicateIds(ipam.Subnets, item => item.Id, (item, id) => item.Id = id, "Subnet", report);
        var changedMaps = NormalizeDuplicateIds(mapDocument.Maps, item => item.Id, (item, id) => item.Id = id, "Network map", report);
        foreach (var map in mapDocument.Maps)
            changedMaps |= NormalizeDuplicateIds(map.Connections, item => item.Id, (item, id) => item.Id = id, "Network map connection", report);

        changedStructure |= ReconcileHierarchy(structure, report);
        changedDevices |= ReconcileDevices(deviceDocument, structure, report);
        changedIpam |= ReconcileIpam(ipam, deviceDocument, structure, report);
        changedMaps |= ReconcileMaps(mapDocument, deviceDocument, structure, report);

        if (changedStructure) await documents.WriteAsync("inventory-structure.json", structure, cancellationToken);
        if (changedDevices) await documents.WriteAsync("devices.json", deviceDocument, cancellationToken);
        if (changedIpam) await documents.WriteAsync("ipam.json", ipam, cancellationToken);
        if (changedMaps) await documents.WriteAsync("network-maps.json", mapDocument, cancellationToken);

        report.RepairedCount = report.Issues.Count(issue => issue.Repaired);
        await documents.WriteAsync("inventory-reference-report.json", report, cancellationToken);
        foreach (var issue in report.Issues.Where(issue => !issue.Repaired))
        {
            logger.LogWarning("Inventory reference needs review: {EntityType} {EntityId} field {Field} references {Reference}.",
                issue.EntityType, issue.EntityId, issue.Field, issue.InvalidReference);
        }
        return report;
    }

    public Task<InventoryReferenceReportDocument> GetReportAsync(CancellationToken cancellationToken = default) =>
        documents.ReadAsync("inventory-reference-report.json", static () => new InventoryReferenceReportDocument(), cancellationToken);

    private static bool NormalizeDuplicateIds<T>(
        IReadOnlyCollection<T> items,
        Func<T, Guid> getId,
        Action<T, Guid> setId,
        string entityType,
        InventoryReferenceReportDocument report)
    {
        var changed = false;
        foreach (var duplicate in items.GroupBy(getId).Where(group => group.Count() > 1))
        {
            var first = true;
            foreach (var item in duplicate)
            {
                if (first) { first = false; continue; }
                var replacementId = Guid.NewGuid();
                setId(item, replacementId);
                Add(report, entityType, replacementId, "Id", duplicate.Key, true,
                    $"Assigned a new stable ID to a duplicate record. Existing references to {duplicate.Key:D} remain attached to the first record; review the ID assignment.");
                changed = true;
            }
        }
        return changed;
    }

    private static bool ReconcileHierarchy(InventoryStructureDocument structure, InventoryReferenceReportDocument report)
    {
        var changed = false;
        var locations = structure.Locations.ToDictionary(item => item.Id);
        foreach (var location in structure.Locations)
        {
            if (location.ParentLocationId is not { } parentId) continue;
            if (!locations.ContainsKey(parentId))
            {
                Add(report, "Location", location.Id, "ParentLocationId", parentId, true, "Removed a missing parent ID; the location itself was preserved.");
                location.ParentLocationId = null;
                changed = true;
            }
        }
        changed |= BreakCycles(structure.Locations, item => item.Id, item => item.ParentLocationId, (item, parent) => item.ParentLocationId = parent,
            "Location", report);

        var groups = structure.Groups.ToDictionary(item => item.Id);
        foreach (var group in structure.Groups)
        {
            if (group.ParentGroupId is not { } parentId) continue;
            if (!groups.ContainsKey(parentId))
            {
                Add(report, "Group", group.Id, "ParentGroupId", parentId, true, "Removed a missing parent ID; the group itself was preserved.");
                group.ParentGroupId = null;
                changed = true;
            }
        }
        changed |= BreakCycles(structure.Groups, item => item.Id, item => item.ParentGroupId, (item, parent) => item.ParentGroupId = parent,
            "Group", report);
        return changed;
    }

    private static bool ReconcileDevices(DeviceDocument document, InventoryStructureDocument structure, InventoryReferenceReportDocument report)
    {
        var changed = false;
        var locations = structure.Locations.ToDictionary(item => item.Id);
        var groups = structure.Groups.Select(item => item.Id).ToHashSet();
        foreach (var device in document.Devices)
        {
            if (device.BuildingId is { } buildingId &&
                (!locations.TryGetValue(buildingId, out var building) || building.Kind != LocationKind.Building ||
                 device.LocationId is { } locationId && building.ParentLocationId != locationId))
            {
                Add(report, "Device", device.Id, "BuildingId", buildingId, true, "Cleared the stale building ID; the saved building name and device data were preserved.");
                device.BuildingId = null;
                changed = true;
            }
            if (device.LocationId is { } siteId && !locations.ContainsKey(siteId))
            {
                Add(report, "Device", device.Id, "LocationId", siteId, true, "Cleared the stale location ID; the saved location name and device data were preserved.");
                device.LocationId = null;
                changed = true;
            }
            if (device.GroupId is { } groupId && !groups.Contains(groupId))
            {
                Add(report, "Device", device.Id, "GroupId", groupId, true, "Cleared the stale group ID; the saved group name and device data were preserved.");
                device.GroupId = null;
                changed = true;
            }
        }
        return changed;
    }

    private static bool ReconcileIpam(
        IpamDocument document,
        DeviceDocument devices,
        InventoryStructureDocument structure,
        InventoryReferenceReportDocument report)
    {
        var changed = false;
        var deviceIds = devices.Devices.Select(item => item.Id).ToHashSet();
        var devicesByIp = devices.Devices.GroupBy(item => item.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var locationIds = structure.Locations.Select(item => item.Id).ToHashSet();
        var buildingIds = structure.Locations.Where(item => item.Kind == LocationKind.Building).Select(item => item.Id).ToHashSet();
        var groupIds = structure.Groups.Select(item => item.Id).ToHashSet();
        var subnetIds = document.Subnets.Select(item => item.Id).ToHashSet();

        foreach (var record in document.Records)
        {
            if (record.DeviceId is { } deviceId && !deviceIds.Contains(deviceId))
            {
                if (devicesByIp.TryGetValue(record.IpAddress, out var matches) && matches.Length == 1)
                {
                    Add(report, "IP record", record.Id, "DeviceId", deviceId, true, $"Reattached the record to the unique monitored device with IP {record.IpAddress}.");
                    record.DeviceId = matches[0].Id;
                }
                else
                {
                    Add(report, "IP record", record.Id, "DeviceId", deviceId, true, "Cleared the stale device ID; IP inventory fields were preserved.");
                    record.DeviceId = null;
                }
                changed = true;
            }
            changed |= ClearInvalidId(record.Id, "IP record", "SubnetId", record.SubnetId, subnetIds, value => record.SubnetId = value, report,
                "Cleared the missing subnet ID; the IP record was preserved.");
            changed |= ClearInvalidId(record.Id, "IP record", "LocationId", record.LocationId, locationIds, value => record.LocationId = value, report,
                "Cleared the missing location ID; the IP record was preserved.");
            changed |= ClearInvalidId(record.Id, "IP record", "BuildingId", record.BuildingId, buildingIds, value => record.BuildingId = value, report,
                "Cleared the missing building ID; the IP record was preserved.");
            changed |= ClearInvalidId(record.Id, "IP record", "GroupId", record.GroupId, groupIds, value => record.GroupId = value, report,
                "Cleared the missing group ID; the IP record was preserved.");
        }

        foreach (var subnet in document.Subnets)
        {
            changed |= ClearInvalidId(subnet.Id, "Subnet", "LocationId", subnet.LocationId, locationIds, value => subnet.LocationId = value, report,
                "Cleared the missing location ID; the subnet record was preserved.");
            changed |= ClearInvalidId(subnet.Id, "Subnet", "BuildingId", subnet.BuildingId, buildingIds, value => subnet.BuildingId = value, report,
                "Cleared the missing building ID; the subnet record was preserved.");
        }
        return changed;
    }

    private static bool ReconcileMaps(
        NetworkMapDocument document,
        DeviceDocument devices,
        InventoryStructureDocument structure,
        InventoryReferenceReportDocument report)
    {
        var changed = false;
        var deviceIds = devices.Devices.Select(item => item.Id).ToHashSet();
        var locationIds = structure.Locations.Select(item => item.Id).ToHashSet();
        var buildingIds = structure.Locations.Where(item => item.Kind == LocationKind.Building).Select(item => item.Id).ToHashSet();
        foreach (var map in document.Maps)
        {
            changed |= ClearInvalidId(map.Id, "Network map", "LocationId", map.LocationId, locationIds, value => map.LocationId = value, report,
                "Cleared the missing location ID; the map and layout were preserved.");
            changed |= ClearInvalidId(map.Id, "Network map", "BuildingId", map.BuildingId, buildingIds, value => map.BuildingId = value, report,
                "Cleared the missing building ID; the map and layout were preserved.");
            var nodeIds = map.Nodes.Select(node => node.DeviceId).ToHashSet();
            foreach (var node in map.Nodes.Where(node => !deviceIds.Contains(node.DeviceId)))
            {
                Add(report, "Network map", map.Id, "Nodes.DeviceId", node.DeviceId, false,
                    "The referenced device is missing. The map node and connections were preserved for administrator review.");
            }
            foreach (var connection in map.Connections.Where(connection => !nodeIds.Contains(connection.FromDeviceId) || !nodeIds.Contains(connection.ToDeviceId)))
            {
                Add(report, "Network map", map.Id, "Connections", connection.Id, false,
                    "A connection endpoint has no node. The connection was preserved for administrator review.");
            }
        }
        return changed;
    }

    private static bool ClearInvalidId(
        Guid entityId,
        string entityType,
        string field,
        Guid? current,
        HashSet<Guid> validIds,
        Action<Guid?> setValue,
        InventoryReferenceReportDocument report,
        string detail)
    {
        if (current is not { } id || validIds.Contains(id)) return false;
        Add(report, entityType, entityId, field, id, true, detail);
        setValue(null);
        return true;
    }

    private static bool BreakCycles<T>(
        IReadOnlyCollection<T> items,
        Func<T, Guid> getId,
        Func<T, Guid?> getParent,
        Action<T, Guid?> setParent,
        string entityType,
        InventoryReferenceReportDocument report)
    {
        var byId = items.ToDictionary(getId);
        var cut = new HashSet<Guid>();
        foreach (var start in items)
        {
            var path = new List<Guid>();
            var indexes = new Dictionary<Guid, int>();
            var current = getId(start);
            while (byId.TryGetValue(current, out var item))
            {
                if (indexes.TryGetValue(current, out var cycleStart))
                {
                    var breakId = path.Skip(cycleStart).Max();
                    if (cut.Add(breakId))
                    {
                        setParent(byId[breakId], null);
                        Add(report, entityType, breakId, "ParentId", current, true, "Broke a hierarchy cycle by clearing one parent ID; all nodes were preserved.");
                    }
                    break;
                }
                indexes[current] = path.Count;
                path.Add(current);
                if (getParent(item) is not { } parentId) break;
                current = parentId;
            }
        }
        return cut.Count > 0;
    }

    private static void Add(
        InventoryReferenceReportDocument report,
        string entityType,
        Guid entityId,
        string field,
        Guid invalidReference,
        bool repaired,
        string detail) =>
        report.Issues.Add(new InventoryReferenceIssue
        {
            EntityType = entityType,
            EntityId = entityId.ToString("D"),
            Field = field,
            InvalidReference = invalidReference.ToString("D"),
            Repaired = repaired,
            Detail = detail
        });
}
