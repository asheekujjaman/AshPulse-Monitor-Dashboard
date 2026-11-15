using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Storage.Setup;

public sealed class LegacyHierarchyMigrator(IDeviceRepository devices, IInventoryStructureRepository structure)
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var all = await devices.GetAllAsync(cancellationToken);
        var updates = new List<DeviceAssignmentUpdate>();
        foreach (var device in all)
        {
            if ((device.LocationId is not null || string.IsNullOrWhiteSpace(device.Location)) &&
                (device.BuildingId is not null || string.IsNullOrWhiteSpace(device.Building)) &&
                (device.GroupId is not null || string.IsNullOrWhiteSpace(device.Group)))
            {
                continue;
            }

            var resolved = await structure.ResolveOrCreateLegacyAssignmentAsync(
                device.Location,
                device.Building,
                device.Group,
                cancellationToken);
            var locationId = device.LocationId ?? resolved.LocationId;
            var buildingId = device.BuildingId ?? resolved.BuildingId;
            var groupId = device.GroupId ?? resolved.GroupId;
            if (locationId != device.LocationId || buildingId != device.BuildingId || groupId != device.GroupId)
            {
                updates.Add(new DeviceAssignmentUpdate(
                    device.Id,
                    locationId,
                    buildingId,
                    groupId,
                    device.Location,
                    device.Building,
                    device.Group));
            }
        }

        if (updates.Count > 0)
        {
            await devices.UpdateAssignmentsAsync(updates, cancellationToken);
        }
    }
}
