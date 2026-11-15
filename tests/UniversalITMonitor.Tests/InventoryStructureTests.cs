using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Storage.Json;
using UniversalITMonitor.Storage.Setup;

namespace UniversalITMonitor.Tests;

[TestClass]
public sealed class InventoryStructureTests
{
    [TestMethod]
    public async Task LegacyHierarchyMigration_PreservesDeviceDataAndIsIdempotent()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var checkedUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        var device = new NetworkDevice
        {
            Name = "Legacy router",
            IpAddress = "192.0.2.61",
            Location = "Dhaka",
            Building = "Building A",
            Group = "Network Devices",
            Status = DeviceStatus.Up,
            LastCheckUtc = checkedUtc,
            LastSuccessfulCheckUtc = checkedUtc,
            CurrentStateSinceUtc = checkedUtc,
            ResponseTimeMilliseconds = 8
        };
        Assert.AreEqual(DeviceWriteResult.Success, await fixture.Repository.CreateAsync(device));
        var migrator = new LegacyHierarchyMigrator(fixture.Repository, fixture.Structure);

        await migrator.MigrateAsync();
        var migrated = await fixture.Repository.GetAsync(device.Id);
        Assert.IsNotNull(migrated);
        Assert.IsNotNull(migrated.LocationId);
        Assert.IsNotNull(migrated.BuildingId);
        Assert.IsNotNull(migrated.GroupId);
        Assert.AreEqual(checkedUtc, migrated.LastCheckUtc);
        Assert.AreEqual(DeviceStatus.Up, migrated.Status);

        var firstIds = new[] { migrated.LocationId, migrated.BuildingId, migrated.GroupId };
        await migrator.MigrateAsync();
        var migratedAgain = await fixture.Repository.GetAsync(device.Id);
        Assert.IsNotNull(migratedAgain);
        CollectionAssert.AreEqual(firstIds, new[] { migratedAgain.LocationId, migratedAgain.BuildingId, migratedAgain.GroupId });

        var displayed = await fixture.Service.GetAsync(device.Id);
        Assert.IsNotNull(displayed);
        Assert.AreEqual("Dhaka", displayed.Location);
        Assert.AreEqual("Dhaka / Building A", displayed.Building);
        Assert.AreEqual("Network Devices", displayed.Group);
    }

    [TestMethod]
    public async Task NestedLocationsAndGroupsCountActualDeviceStatuses()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var management = new InventoryStructureService(fixture.Structure, fixture.Repository);
        var site = new ManagedLocation { Name = "Dhaka", Kind = LocationKind.Site };
        var building = new ManagedLocation { Name = "Building A", Kind = LocationKind.Building, ParentLocationId = site.Id };
        var parentGroup = new ManagedGroup { Name = "Network Devices" };
        var childGroup = new ManagedGroup { Name = "Routers", ParentGroupId = parentGroup.Id };
        Assert.AreEqual(StructureWriteResult.Success, await management.CreateLocationAsync(site));
        Assert.AreEqual(StructureWriteResult.Success, await management.CreateLocationAsync(building));
        Assert.AreEqual(StructureWriteResult.Success, await management.CreateGroupAsync(parentGroup));
        Assert.AreEqual(StructureWriteResult.Success, await management.CreateGroupAsync(childGroup));

        var first = await fixture.Service.CreateAsync(new DeviceEditInput
        {
            Name = "Router up", IpAddress = "192.0.2.62", Type = DeviceType.Router,
            LocationId = site.Id, BuildingId = building.Id, GroupId = childGroup.Id
        });
        var second = await fixture.Service.CreateAsync(new DeviceEditInput
        {
            Name = "Router down", IpAddress = "192.0.2.63", Type = DeviceType.Router,
            LocationId = site.Id, BuildingId = building.Id, GroupId = childGroup.Id
        });
        Assert.IsTrue(first.Succeeded);
        Assert.IsTrue(second.Succeeded);
        var stored = await fixture.Repository.GetAllAsync();
        var checkedUtc = DateTimeOffset.UtcNow;
        await fixture.Repository.RecordChecksAsync(
        [
            new DeviceCheckResult { DeviceId = stored.Single(item => item.Name == "Router up").Id, IpAddress = "192.0.2.62", CheckedUtc = checkedUtc, Status = DeviceStatus.Up, ResponseTimeMilliseconds = 3 },
            new DeviceCheckResult { DeviceId = stored.Single(item => item.Name == "Router down").Id, IpAddress = "192.0.2.63", CheckedUtc = checkedUtc, Status = DeviceStatus.Down, Error = "timeout" }
        ]);

        var locationSummary = (await management.GetLocationSummariesAsync()).Single(item => item.Location.Id == site.Id);
        var buildingSummary = (await management.GetLocationSummariesAsync()).Single(item => item.Location.Id == building.Id);
        var groupSummary = (await management.GetGroupSummariesAsync()).Single(item => item.Group.Id == parentGroup.Id);
        Assert.AreEqual(2, locationSummary.DeviceCount);
        Assert.AreEqual(2, buildingSummary.DeviceCount);
        Assert.AreEqual(1, locationSummary.UpCount);
        Assert.AreEqual(1, locationSummary.DownCount);
        Assert.AreEqual(2, groupSummary.DeviceCount);
        Assert.AreEqual("Network Devices / Routers", (await management.GetGroupSummariesAsync()).Single(item => item.Group.Id == childGroup.Id).Path);
    }

    [TestMethod]
    public async Task HierarchyRejectsSiblingDuplicatesAndCycles_AndProtectsAssignedDevicesOnDelete()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var management = new InventoryStructureService(fixture.Structure, fixture.Repository);
        var site = new ManagedLocation { Name = "Dhaka", Kind = LocationKind.Site };
        var otherSite = new ManagedLocation { Name = "Gazipur", Kind = LocationKind.Site };
        var child = new ManagedLocation { Name = "Building A", Kind = LocationKind.Building, ParentLocationId = site.Id };
        Assert.AreEqual(StructureWriteResult.Success, await management.CreateLocationAsync(site));
        Assert.AreEqual(StructureWriteResult.Success, await management.CreateLocationAsync(otherSite));
        Assert.AreEqual(StructureWriteResult.Success, await management.CreateLocationAsync(child));
        Assert.AreEqual(StructureWriteResult.DuplicateName, await management.CreateLocationAsync(new ManagedLocation { Name = "Dhaka", Kind = LocationKind.Site }));
        Assert.AreEqual(StructureWriteResult.InvalidParent, await management.UpdateLocationAsync(new ManagedLocation { Id = site.Id, Name = "Dhaka", Kind = LocationKind.Site, ParentLocationId = child.Id }));

        var group = new ManagedGroup { Name = "Operations" };
        Assert.AreEqual(StructureWriteResult.Success, await management.CreateGroupAsync(group));
        var deviceResult = await fixture.Service.CreateAsync(new DeviceEditInput
        {
            Name = "Assigned device", IpAddress = "192.0.2.64", Type = DeviceType.Server,
            LocationId = site.Id, BuildingId = child.Id, GroupId = group.Id
        });
        Assert.IsTrue(deviceResult.Succeeded);

        var blocked = await management.DeleteLocationAsync(site.Id, null, false);
        Assert.AreEqual(StructureWriteResult.HasChildren, blocked.Result);
        var childDelete = await management.DeleteLocationAsync(child.Id, null, false);
        Assert.AreEqual(StructureWriteResult.InUse, childDelete.Result);
        var stillAssigned = (await fixture.Repository.GetAllAsync()).Single();
        Assert.AreEqual(child.Id, stillAssigned.BuildingId);

        var moved = await management.DeleteLocationAsync(child.Id, otherSite.Id, false);
        Assert.AreEqual(StructureWriteResult.Success, moved.Result);
        var movedDevice = (await fixture.Repository.GetAllAsync()).Single();
        Assert.AreEqual(otherSite.Id, movedDevice.LocationId);
        Assert.IsNull(movedDevice.BuildingId);

        var groupInUse = await management.DeleteGroupAsync(group.Id, null, false);
        Assert.AreEqual(StructureWriteResult.InUse, groupInUse.Result);
        var unassigned = await management.DeleteGroupAsync(group.Id, null, true);
        Assert.AreEqual(StructureWriteResult.Success, unassigned.Result);
        Assert.IsNull((await fixture.Repository.GetAllAsync()).Single().GroupId);
    }
}
