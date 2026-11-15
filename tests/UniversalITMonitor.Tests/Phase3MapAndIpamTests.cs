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
}
