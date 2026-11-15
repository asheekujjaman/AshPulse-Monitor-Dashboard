using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Monitoring;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Application.Ipam;
using UniversalITMonitor.Application.NetworkMaps;
using UniversalITMonitor.Application.Notifications;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Monitoring;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Storage.Json;

namespace UniversalITMonitor.Tests;

[TestClass]
public sealed class Phase2RegressionTests
{
    [TestMethod]
    public async Task DeviceService_CreatesUpdatesAndDeletesDevices_AndRejectsDuplicateAddresses()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();

        var created = await fixture.Service.CreateAsync(NewInput("Router A", "192.0.2.20"));
        Assert.AreEqual(DeviceWriteResult.Success, created.Result);

        var duplicate = await fixture.Service.CreateAsync(NewInput("Router B", "192.0.2.20"));
        Assert.AreEqual(DeviceWriteResult.DuplicateIpAddress, duplicate.Result);

        var device = (await fixture.Repository.GetAllAsync()).Single();
        var updated = await fixture.Service.UpdateAsync(NewInput("Router A renamed", "192.0.2.21", device.Id));
        Assert.AreEqual(DeviceWriteResult.Success, updated.Result);
        var current = await fixture.Service.GetAsync(device.Id);
        Assert.IsNotNull(current);
        Assert.AreEqual("Router A renamed", current.Name);
        Assert.AreEqual("192.0.2.21", current.IpAddress);

        Assert.IsTrue(await fixture.Service.DeleteAsync(device.Id));
        Assert.IsNull(await fixture.Service.GetAsync(device.Id));
    }

    [TestMethod]
    public async Task DeviceService_RejectsInvalidIpAddress()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();

        var result = await fixture.Service.CreateAsync(NewInput("Bad address", "not-an-ip"));

        Assert.AreEqual(DeviceWriteResult.ValidationFailed, result.Result);
        Assert.IsEmpty(await fixture.Repository.GetAllAsync());
    }

    [TestMethod]
    public async Task DeviceService_ProtectsV2cAndV3Credentials_AndOnlyReleasesThemForMonitoring()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var v2c = NewInput("V2 device", "192.0.2.30");
        v2c.MonitoringMethod = MonitoringMethod.IcmpAndSnmp;
        v2c.SnmpVersion = SnmpVersion.V2c;
        v2c.SnmpCommunity = "community-secret-phase2";
        Assert.AreEqual(DeviceWriteResult.Success, (await fixture.Service.CreateAsync(v2c)).Result);

        var v3 = NewInput("V3 device", "192.0.2.31");
        v3.MonitoringMethod = MonitoringMethod.IcmpAndSnmp;
        v3.SnmpVersion = SnmpVersion.V3;
        v3.SnmpUserName = "monitor-user";
        v3.SnmpSecurityLevel = SnmpSecurityLevel.AuthPriv;
        v3.SnmpAuthenticationPassword = "auth-secret-phase2";
        v3.SnmpPrivacyPassword = "privacy-secret-phase2";
        Assert.AreEqual(DeviceWriteResult.Success, (await fixture.Service.CreateAsync(v3)).Result);

        var persisted = await File.ReadAllTextAsync(Path.Combine(fixture.DataDirectory, "devices.json"));
        Assert.IsFalse(persisted.Contains(v2c.SnmpCommunity, StringComparison.Ordinal));
        Assert.IsFalse(persisted.Contains(v3.SnmpAuthenticationPassword, StringComparison.Ordinal));
        Assert.IsFalse(persisted.Contains(v3.SnmpPrivacyPassword, StringComparison.Ordinal));
        StringAssert.Contains(persisted, "protectedCommunity");
        StringAssert.Contains(persisted, "protectedAuthenticationPassword");
        StringAssert.Contains(persisted, "protectedPrivacyPassword");

        var safeView = await fixture.Service.GetAllAsync();
        Assert.IsTrue(safeView.Single(device => device.Name == "V2 device").HasSnmpCommunity);
        Assert.IsTrue(safeView.Single(device => device.Name == "V3 device").HasSnmpAuthenticationPassword);
        var safeJson = JsonSerializer.Serialize(safeView);
        Assert.IsFalse(safeJson.Contains(v2c.SnmpCommunity, StringComparison.Ordinal));
        Assert.IsFalse(safeJson.Contains(v3.SnmpAuthenticationPassword, StringComparison.Ordinal));
        Assert.IsFalse(safeJson.Contains(v3.SnmpPrivacyPassword, StringComparison.Ordinal));

        var monitorDevices = await fixture.Service.GetForMonitoringAsync();
        Assert.AreEqual(v2c.SnmpCommunity, monitorDevices.Single(device => device.Name == "V2 device").Snmp.ProtectedCommunity);
        var monitorV3 = monitorDevices.Single(device => device.Name == "V3 device");
        Assert.AreEqual(v3.SnmpAuthenticationPassword, monitorV3.Snmp.ProtectedAuthenticationPassword);
        Assert.AreEqual(v3.SnmpPrivacyPassword, monitorV3.Snmp.ProtectedPrivacyPassword);
    }

    [TestMethod]
    public async Task MonitoringWorker_PersistsIcmpStateAndStatusTransitions()
    {
        using var fixture = await DeviceServiceFixture.CreateAsync();
        var device = new NetworkDevice { Name = "Cycle test", IpAddress = "192.0.2.40" };
        Assert.AreEqual(DeviceWriteResult.Success, await fixture.Repository.CreateAsync(device));
        await fixture.Documents.WriteAsync("settings.json", new ApplicationSettings
        {
            PollingIntervalSeconds = 3600,
            MaximumConcurrentChecks = 1,
            PingTimeoutMilliseconds = 1000
        });

        var probe = new SequentialIcmpProbe();
        using var worker = new MonitoringWorker(
            fixture.Service,
            fixture.Repository,
            fixture.History,
            fixture.Metrics,
            fixture.Samples,
            new RecordingTestNotificationService(),
            fixture.Documents,
            probe,
            new UnexpectedSnmpCollector(),
            fixture.CycleTrigger,
            NullLogger<MonitoringWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await probe.FirstCheck.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.CycleTrigger.RequestImmediateCycle();
        await probe.SecondCheck.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(fixture.Repository, device.Id, DeviceStatus.Down);
        await WaitForHistoryCountAsync(fixture.History, device.Id, 2);
        await worker.StopAsync(CancellationToken.None);

        var stored = await fixture.Repository.GetAsync(device.Id);
        Assert.IsNotNull(stored);
        Assert.AreEqual(DeviceStatus.Down, stored.Status);
        Assert.IsNotNull(stored.LastFailedCheckUtc);
        Assert.IsNull(stored.ResponseTimeMilliseconds);

        var history = await fixture.History.GetForDeviceAsync(device.Id, 20);
        Assert.HasCount(2, history);
        CollectionAssert.AreEqual(new[] { DeviceStatus.Down, DeviceStatus.Up }, history.Select(item => item.Status).ToArray());
        Assert.AreEqual(DeviceStatus.Up, history[0].PreviousStatus);
        Assert.AreEqual(DeviceStatus.Unknown, history[1].PreviousStatus);
        var samples = await fixture.Samples.GetWindowAsync(device.Id, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));
        Assert.HasCount(2, samples.Samples);
        Assert.AreEqual(12L, samples.Samples[0].ResponseTimeMilliseconds);
        Assert.IsNull(samples.Samples[1].ResponseTimeMilliseconds);
    }

    private static DeviceEditInput NewInput(string name, string ipAddress, Guid? id = null) => new()
    {
        Id = id,
        Name = name,
        IpAddress = ipAddress,
        Type = DeviceType.Router
    };

    private static async Task WaitForStatusAsync(IDeviceRepository repository, Guid id, DeviceStatus expected)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await repository.GetAsync(id))?.Status == expected)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"The device did not reach status {expected} before the timeout.");
    }

    private static async Task WaitForHistoryCountAsync(IMonitoringHistoryStore history, Guid id, int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await history.GetForDeviceAsync(id, 20)).Count >= expectedCount) return;
            await Task.Delay(20);
        }

        Assert.Fail($"The device did not persist {expectedCount} history entries before the timeout.");
    }
}

internal sealed class DeviceServiceFixture : IDisposable
{
    private readonly TemporaryTestDirectory _directory;

    private DeviceServiceFixture(TemporaryTestDirectory directory)
    {
        _directory = directory;
        DataDirectory = directory.Path;
        Documents = new FileJsonDocumentStore(DataDirectory);
        Repository = new JsonDeviceRepository(Documents);
        Structure = new JsonInventoryStructureRepository(Documents);
        History = new JsonMonitoringHistoryStore(DataDirectory);
        Samples = new JsonMonitoringSampleStore(DataDirectory);
        Metrics = new JsonSnmpMetricsStore(DataDirectory);
        MapRepository = new JsonNetworkMapRepository(Documents);
        IpamRepository = new JsonIpamRepository(Documents);
        StructureService = new InventoryStructureService(Structure, Repository);
        CycleTrigger = new TestCycleTrigger();
        var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(DataDirectory, "keys")));
        Service = new DeviceService(Repository, History, new DataProtectionSecretProtector(provider), Metrics, CycleTrigger, Structure, MapRepository, IpamRepository);
        MapService = new NetworkMapService(MapRepository, Repository, Structure);
        IpamService = new IpamService(IpamRepository, Service, StructureService);
    }

    public string DataDirectory { get; }
    public IJsonDocumentStore Documents { get; }
    public JsonDeviceRepository Repository { get; }
    public JsonInventoryStructureRepository Structure { get; }
    public JsonMonitoringHistoryStore History { get; }
    public JsonMonitoringSampleStore Samples { get; }
    public JsonSnmpMetricsStore Metrics { get; }
    public JsonNetworkMapRepository MapRepository { get; }
    public JsonIpamRepository IpamRepository { get; }
    public InventoryStructureService StructureService { get; }
    public NetworkMapService MapService { get; }
    public IpamService IpamService { get; }
    public TestCycleTrigger CycleTrigger { get; }
    public DeviceService Service { get; }

    public static async Task<DeviceServiceFixture> CreateAsync()
    {
        var fixture = new DeviceServiceFixture(new TemporaryTestDirectory());
        await fixture.Documents.EnsureAsync("devices.json", new DeviceDocument());
        await fixture.Documents.EnsureAsync("inventory-structure.json", new InventoryStructureDocument());
        await fixture.Documents.EnsureAsync("settings.json", new ApplicationSettings());
        await fixture.Documents.EnsureAsync("network-maps.json", new NetworkMapDocument());
        await fixture.Documents.EnsureAsync("ipam.json", new IpamDocument());
        return fixture;
    }

    public void Dispose() => _directory.Dispose();
}

internal sealed class TemporaryTestDirectory : IDisposable
{
    public TemporaryTestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UniversalITMonitor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed class TestCycleTrigger : IMonitoringCycleTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void RequestImmediateCycle()
    {
        if (_signal.CurrentCount == 0)
        {
            _signal.Release();
        }
    }

    public Task<bool> WaitForNextCycleAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        _signal.WaitAsync(timeout, cancellationToken);
}

internal sealed class SequentialIcmpProbe : IIcmpProbe
{
    private int _calls;

    public TaskCompletionSource<bool> FirstCheck { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> SecondCheck { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IcmpProbeResult> CheckAsync(string ipAddress, int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _calls);
        if (call == 1)
        {
            FirstCheck.TrySetResult(true);
            return Task.FromResult(new IcmpProbeResult(true, 12, null));
        }

        if (call == 2)
        {
            SecondCheck.TrySetResult(true);
            return Task.FromResult(new IcmpProbeResult(false, null, "timeout"));
        }

        return Task.FromResult(new IcmpProbeResult(false, null, "unexpected extra check"));
    }
}

internal sealed class UnexpectedSnmpCollector : ISnmpMetricsCollector
{
    public Task<SnmpMetrics> CollectAsync(NetworkDevice device, CancellationToken cancellationToken = default) =>
        throw new AssertFailedException("SNMP must not run for an ICMP-only device.");
}

internal sealed class RecordingTestNotificationService : INotificationService
{
    public List<MonitoringHistoryEntry> QueuedTransitions { get; } = [];
    public Task<NotificationSettingsView> GetSettingsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<NotificationOperationResult> SaveSettingsAsync(NotificationSettingsInput input, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<NotificationSendResult> TestSendAsync(NotificationChannel channel, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task ProcessPendingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> RetryDeadLetterAsync(Guid recordId, NotificationChannel channel, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<IReadOnlyList<NotificationRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NotificationRecord>>([]);

    public Task QueueTransitionAsync(MonitoringHistoryEntry transition, CancellationToken cancellationToken = default)
    {
        QueuedTransitions.Add(transition);
        return Task.CompletedTask;
    }
}
