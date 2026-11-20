using UniversalITMonitor.Application.Ipam;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Storage.Json;

namespace UniversalITMonitor.Tests;

[TestClass]
public sealed class Phase3MapAndIpamTests
{
    [TestMethod]
    public async Task NetworkMap_PersistsLayoutAndRequiresExistingDeviceReferences()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(new UniversalITMonitor.Application.Devices.DeviceEditInput
        {
            Name = "Map router", IpAddress = "192.0.2.110", Type = DeviceType.Router
        });
        Assert.IsTrue(created.Succeeded);
        var device = (await fixture.Repository.GetAllAsync()).Single();
        var map = new NetworkMap
        {
            Name = "Dhaka Core",
            Nodes = [new NetworkMapNode { DeviceId = device.Id, Icon = MapIconKind.Router, X = 290, Y = 175, Width = 164, Height = 104 }]
        };

        var saved = await fixture.MapService.SaveAsync(map);
        Assert.AreEqual(MapWriteResult.Success, saved.Result);
        Assert.AreEqual(map.Id, saved.MapId);

        var reopenedRepository = new JsonNetworkMapRepository(new FileJsonDocumentStore(fixture.DataDirectory));
        var reopened = await reopenedRepository.GetAsync(map.Id);
        Assert.IsNotNull(reopened);
        Assert.AreEqual(290d, reopened.Nodes.Single().X);
        Assert.AreEqual(device.Id, reopened.Nodes.Single().DeviceId);

        var missingId = Guid.NewGuid();
        map.Nodes.Add(new NetworkMapNode { DeviceId = missingId, Icon = MapIconKind.Server });
        var invalid = await fixture.MapService.SaveAsync(map);
        Assert.AreEqual(MapWriteResult.Invalid, invalid.Result);
        CollectionAssert.Contains(invalid.MissingDeviceIds!.ToArray(), missingId);

        var preview = await fixture.MapService.PreviewImportAsync(System.Text.Json.JsonSerializer.Serialize(map));
        Assert.IsNotNull(preview.Map);
        CollectionAssert.Contains(preview.MissingDeviceIds.ToArray(), missingId);

        var oversized = await fixture.MapService.PreviewImportAsync(new string(' ', 2_000_001));
        StringAssert.Contains(oversized.Error!, "2 MB or smaller");
    }

    [TestMethod]
    public async Task DeletingDeviceRemovesItsPersistedMapNodeAndConnections()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var first = await fixture.Service.CreateAsync(new UniversalITMonitor.Application.Devices.DeviceEditInput { Name = "First", IpAddress = "192.0.2.111", Type = DeviceType.Router });
        var second = await fixture.Service.CreateAsync(new UniversalITMonitor.Application.Devices.DeviceEditInput { Name = "Second", IpAddress = "192.0.2.112", Type = DeviceType.Server });
        Assert.IsTrue(first.Succeeded && second.Succeeded);
        var devices = await fixture.Repository.GetAllAsync();
        var map = new NetworkMap
        {
            Name = "Delete reference test",
            Nodes = devices.Select(device => new NetworkMapNode { DeviceId = device.Id }).ToList(),
            Connections = [new NetworkMapConnection { FromDeviceId = devices[0].Id, ToDeviceId = devices[1].Id }]
        };
        Assert.AreEqual(MapWriteResult.Success, (await fixture.MapService.SaveAsync(map)).Result);

        Assert.IsTrue(await fixture.Service.DeleteAsync(devices[0].Id));
        var persisted = await fixture.MapRepository.GetAsync(map.Id);
        Assert.IsNotNull(persisted);
        Assert.AreEqual(devices[1].Id, persisted.Nodes.Single().DeviceId);
        Assert.IsEmpty(persisted.Connections);
    }

    [TestMethod]
    public async Task Ipam_ValidatesAndCanonicalizesIpAddressesAndCidrNetworks()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var subnet = new IpamSubnet { Cidr = "192.0.2.63/24", VlanId = 20, VlanName = "Operations" };
        var subnetResult = await fixture.IpamService.SaveSubnetAsync(subnet);
        Assert.IsTrue(subnetResult.Success, subnetResult.Error);
        Assert.AreEqual("192.0.2.0/24", subnet.Cidr);
        Assert.AreEqual("192.0.2.0", subnet.NetworkAddress);

        var record = new IpamRecord
        {
            IpAddress = "192.0.2.7", SubnetId = subnet.Id, AssignmentStatus = IpAssignmentStatus.Assigned,
            VlanId = 20, MacAddress = "0011.2233.4455", DeviceName = "Printer 7"
        };
        var saved = await fixture.IpamService.SaveRecordAsync(record);
        Assert.IsTrue(saved.Success, saved.Error);
        Assert.AreEqual("192.0.2.7", record.IpAddress);
        Assert.AreEqual("00:11:22:33:44:55", record.MacAddress);

        var duplicate = await fixture.IpamService.SaveRecordAsync(new IpamRecord { IpAddress = "192.0.2.7" });
        Assert.IsFalse(duplicate.Success);
        StringAssert.Contains(duplicate.Error!, "already exists");

        var outsideSubnet = await fixture.IpamService.SaveRecordAsync(new IpamRecord { IpAddress = "192.0.3.2", SubnetId = subnet.Id });
        Assert.IsFalse(outsideSubnet.Success);
        StringAssert.Contains(outsideSubnet.Error!, "outside the selected subnet");

        var invalidIp = await fixture.IpamService.SaveRecordAsync(new IpamRecord { IpAddress = "not-an-ip" });
        Assert.IsFalse(invalidIp.Success);
        StringAssert.Contains(invalidIp.Error!, "valid IPv4 or IPv6");

        var invalidCidr = await fixture.IpamService.SaveSubnetAsync(new IpamSubnet { Cidr = "192.0.2.0/33" });
        Assert.IsFalse(invalidCidr.Success);

        var moveSubnet = await fixture.IpamService.SaveSubnetAsync(new IpamSubnet { Id = subnet.Id, Cidr = "192.0.3.0/24" });
        Assert.IsFalse(moveSubnet.Success, "A subnet cannot be moved away from addresses already assigned to it.");

        var reopenedRepository = new JsonIpamRepository(new FileJsonDocumentStore(fixture.DataDirectory));
        Assert.AreEqual("192.0.2.0/24", (await reopenedRepository.GetSubnetAsync(subnet.Id))?.Cidr);
        Assert.AreEqual("192.0.2.7", (await reopenedRepository.GetRecordAsync(record.Id))?.IpAddress);

        Assert.IsTrue(IpamService.TryNormalizeCidr("2001:db8::beef/64", out var ipv6Network, out var prefix, out var canonical));
        Assert.AreEqual("2001:db8::", ipv6Network);
        Assert.AreEqual(64, prefix);
        Assert.AreEqual("2001:db8::/64", canonical);
        Assert.IsFalse(IpamService.TryNormalizeCidr("2001:db8::/129", out _, out _, out _));
    }

    [TestMethod]
    public async Task IpamCsvPreviewReportsInvalidAndDuplicateRows_AndImportNeverOverwrites()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var existing = new IpamRecord { IpAddress = "198.51.100.10", DeviceName = "Existing record", AssignmentStatus = IpAssignmentStatus.Reserved };
        Assert.IsTrue((await fixture.IpamService.SaveRecordAsync(existing)).Success);
        const string csv = "IP,Device,Assignment\r\n198.51.100.11,New device,Assigned\r\n198.51.100.10,Attempted overwrite,Assigned\r\nnot-an-ip,Bad row,Assigned\r\n198.51.100.11,Duplicate in file,Available\r\n";
        var mapping = new Dictionary<string, string?>
        {
            ["IpAddress"] = "IP",
            ["DeviceName"] = "Device",
            ["AssignmentStatus"] = "Assignment"
        };

        var preview = await fixture.IpamService.PreviewCsvImportAsync(csv, mapping);
        Assert.AreEqual(1, preview.ValidCount);
        Assert.AreEqual(1, preview.InvalidCount);
        Assert.AreEqual(2, preview.DuplicateCount);
        Assert.IsTrue(preview.Rows.Any(row => row.Errors.Any(error => error.Contains("valid IP address", StringComparison.OrdinalIgnoreCase))));

        var report = await fixture.IpamService.ImportCsvAsync(csv, mapping);
        Assert.AreEqual(1, report.ImportedCount);
        Assert.AreEqual(1, report.InvalidCount);
        Assert.AreEqual(2, report.DuplicateCount);
        var records = await fixture.IpamService.GetRecordsAsync();
        Assert.HasCount(2, records);
        Assert.AreEqual("Existing record", records.Single(record => record.IpAddress == "198.51.100.10").DeviceName);
        Assert.AreEqual("New device", records.Single(record => record.IpAddress == "198.51.100.11").DeviceName);

        var oversizedField = "IP,Description\r\n198.51.100.12," + new string('x', 4_097);
        var limited = await fixture.IpamService.PreviewCsvImportAsync(oversizedField, new Dictionary<string, string?> { ["IpAddress"] = "IP" });
        StringAssert.Contains(limited.Error!, "4,096 characters");
    }

    [TestMethod]
    public async Task LinkedIpamRecordUsesCurrentDeviceIdentityWithoutInferringAssignmentStatus()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var result = await fixture.Service.CreateAsync(new UniversalITMonitor.Application.Devices.DeviceEditInput
        {
            Name = "Live device", IpAddress = "203.0.113.40", Type = DeviceType.Server
        });
        Assert.IsTrue(result.Succeeded);
        var device = (await fixture.Repository.GetAllAsync()).Single();
        await fixture.Repository.RecordChecksAsync([new DeviceCheckResult
        {
            DeviceId = device.Id,
            IpAddress = device.IpAddress,
            CheckedUtc = DateTimeOffset.UtcNow,
            Status = DeviceStatus.Up,
            ResponseTimeMilliseconds = 6
        }]);

        var record = new IpamRecord { DeviceId = device.Id, IpAddress = device.IpAddress, AssignmentStatus = IpAssignmentStatus.Unknown };
        Assert.IsTrue((await fixture.IpamService.SaveRecordAsync(record)).Success);
        var displayed = await fixture.IpamService.GetRecordAsync(record.Id);

        Assert.IsNotNull(displayed);
        Assert.AreEqual(device.Name, displayed.DeviceName);
        Assert.AreEqual(device.IpAddress, displayed.IpAddress);
        Assert.AreEqual(DeviceType.Server.ToString(), displayed.DeviceType);
        Assert.AreEqual(IpAssignmentStatus.Unknown, displayed.AssignmentStatus);

        var updated = await fixture.Service.UpdateAsync(new UniversalITMonitor.Application.Devices.DeviceEditInput
        {
            Id = device.Id, Name = "Live device renamed", IpAddress = "203.0.113.41", Type = DeviceType.Server
        });
        Assert.IsTrue(updated.Succeeded, updated.Error);
        var synchronized = await fixture.IpamService.GetRecordAsync(record.Id);
        Assert.IsNotNull(synchronized);
        Assert.AreEqual("Live device renamed", synchronized.DeviceName);
        Assert.AreEqual("203.0.113.41", synchronized.IpAddress);

        Assert.IsTrue(await fixture.Service.DeleteAsync(device.Id));
        var retained = await fixture.IpamRepository.GetRecordAsync(record.Id);
        Assert.IsNotNull(retained);
        Assert.IsNull(retained.DeviceId);
        Assert.AreEqual("203.0.113.41", retained.IpAddress);
        Assert.AreEqual("Live device renamed", retained.DeviceName);
    }

    [TestMethod]
    public async Task IpamAllocationTransitionsAreAtomicAndPreserveHistory()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var subnet = new IpamSubnet { Cidr = "192.0.2.0/29" };
        Assert.IsTrue((await fixture.IpamService.SaveSubnetAsync(subnet)).Success);

        var reserved = new IpamRecord
        {
            IpAddress = "192.0.2.2", SubnetId = subnet.Id, AssignmentStatus = IpAssignmentStatus.Reserved,
            AssignedTo = "Future owner", ReservationNote = "Keep for onboarding"
        };
        Assert.IsTrue((await fixture.IpamService.ChangeAllocationAsync(reserved, "Reserved", "admin")).Success);

        var wrongOwner = new IpamRecord { IpAddress = "192.0.2.2", SubnetId = subnet.Id, AssignmentStatus = IpAssignmentStatus.Assigned, AssignedTo = "Other user" };
        Assert.IsFalse((await fixture.IpamService.ChangeAllocationAsync(wrongOwner, "Assigned", "admin")).Success);

        var converted = new IpamRecord { IpAddress = "192.0.2.2", SubnetId = subnet.Id, AssignmentStatus = IpAssignmentStatus.Assigned, AssignedTo = "Future owner" };
        Assert.IsTrue((await fixture.IpamService.ChangeAllocationAsync(converted, "Converted", "admin")).Success);

        var released = new IpamRecord { IpAddress = "192.0.2.2", SubnetId = subnet.Id, AssignmentStatus = IpAssignmentStatus.Available };
        Assert.IsTrue((await fixture.IpamService.ChangeAllocationAsync(released, "Released", "admin")).Success);

        var stored = await fixture.IpamRepository.GetRecordAsync(reserved.Id);
        Assert.IsNotNull(stored);
        Assert.AreEqual(IpAssignmentStatus.Available, stored.AssignmentStatus);
        Assert.IsNull(stored.AssignedTo);
        var history = await fixture.IpamService.GetHistoryAsync();
        Assert.HasCount(3, history);
        Assert.IsTrue(history.All(item => item.RecordId == reserved.Id && item.Actor == "admin"));
        Assert.IsTrue(history.Any(item => item.Action == "Reserved" && item.NewStatus == IpAssignmentStatus.Reserved));
    }

    [TestMethod]
    public async Task IpamConcurrentAllocationsForOneAddressAllowOnlyOneWinner()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var subnet = new IpamSubnet { Cidr = "198.51.100.0/29" };
        Assert.IsTrue((await fixture.IpamService.SaveSubnetAsync(subnet)).Success);
        var attempts = Enumerable.Range(0, 2).Select(index => fixture.IpamService.ChangeAllocationAsync(new IpamRecord
        {
            IpAddress = "198.51.100.3", SubnetId = subnet.Id, AssignmentStatus = IpAssignmentStatus.Assigned, AssignedTo = $"User {index}"
        }, "Assigned", "admin"));

        var results = await Task.WhenAll(attempts);
        Assert.AreEqual(1, results.Count(result => result.Success));
        Assert.AreEqual(1, results.Count(result => !result.Success));
        Assert.HasCount(1, (await fixture.IpamRepository.GetRecordsAsync()).Where(record => record.IpAddress == "198.51.100.3"));
        Assert.HasCount(1, await fixture.IpamService.GetHistoryAsync());
    }

    [TestMethod]
    public void IpamIpv4UsableRangeHandlesPointToPointAndSingleAddressSubnets()
    {
        Assert.IsTrue(IpamService.TryNormalizeCidr("192.0.2.0/31", out var network31, out var prefix31, out _));
        var subnet31 = new IpamSubnet { NetworkAddress = network31, PrefixLength = prefix31 };
        Assert.IsTrue(IpamAddressMath.TryGetIpv4UsableRange(subnet31, out var start31, out var end31));
        Assert.AreEqual("192.0.2.0", IpamAddressMath.FromIpv4Number(start31));
        Assert.AreEqual("192.0.2.1", IpamAddressMath.FromIpv4Number(end31));

        Assert.IsTrue(IpamService.TryNormalizeCidr("192.0.2.9/32", out var network32, out var prefix32, out _));
        var subnet32 = new IpamSubnet { NetworkAddress = network32, PrefixLength = prefix32 };
        Assert.IsTrue(IpamAddressMath.TryGetIpv4UsableRange(subnet32, out var start32, out var end32));
        Assert.AreEqual(start32, end32);
        Assert.AreEqual("192.0.2.9", IpamAddressMath.FromIpv4Number(start32));
    }

    [TestMethod]
    public async Task IpamOldJsonDocumentsDefaultHistoryWithoutLosingExistingData()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var legacy = new IpamRecord { Id = Guid.NewGuid(), IpAddress = "203.0.113.25", AssignmentStatus = IpAssignmentStatus.Assigned };
        await File.WriteAllTextAsync(Path.Combine(fixture.DataDirectory, "ipam.json"),
            System.Text.Json.JsonSerializer.Serialize(new IpamDocument { Records = [legacy] }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        var repository = new JsonIpamRepository(new FileJsonDocumentStore(fixture.DataDirectory));

        Assert.IsEmpty(await repository.GetHistoryAsync());
        Assert.AreEqual(legacy.Id, (await repository.GetRecordAsync(legacy.Id))?.Id);
        Assert.AreEqual("203.0.113.25", (await repository.GetRecordAsync(legacy.Id))?.IpAddress);
    }
}
