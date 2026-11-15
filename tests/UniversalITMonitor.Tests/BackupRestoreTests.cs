using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalITMonitor.Application.Backups;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Storage.Json;
using UniversalITMonitor.Storage.Setup;

namespace UniversalITMonitor.Tests;

[TestClass]
public sealed class BackupRestoreTests
{
    [TestMethod]
    public async Task BackupContainsAllJsonAndMonitoringData_AndKeepsCredentialsEncrypted()
    {
        using var root = new TemporaryTestDirectory();
        using var source = await BackupTestEnvironment.CreateAsync(Path.Combine(root.Path, "source"), "source");
        await source.SeedDataAsync("source device", "192.0.2.10", 45);

        var result = await source.Service.CreateManualBackupAsync();

        Assert.IsTrue(result.Succeeded, result.Error);
        var archivePath = Path.Combine(source.BackupDirectory, result.FileName!);
        Assert.IsTrue(File.Exists(archivePath));
        var preview = await source.Service.PreviewAsync(result.FileName!);
        CollectionAssert.Contains(preview.AvailableModules.ToArray(), BackupModule.MonitoringHistory);
        CollectionAssert.Contains(preview.AvailableModules.ToArray(), BackupModule.SnmpMetrics);
        var paths = preview.Files.Select(file => file.Path).ToArray();
        CollectionAssert.Contains(paths, "devices.json");
        CollectionAssert.Contains(paths, "network-maps.json");
        CollectionAssert.Contains(paths, "ipam.json");
        CollectionAssert.Contains(paths, "backup-settings.json");
        CollectionAssert.Contains(paths, "notification-settings.json");
        CollectionAssert.Contains(paths, "users.json");
        CollectionAssert.Contains(paths, "roles.json");
        CollectionAssert.Contains(paths, "branding.json");
        CollectionAssert.Contains(paths, "themes.json");
        Assert.IsTrue(paths.Any(path => path.StartsWith("monitoring-history/", StringComparison.Ordinal)));
        Assert.IsTrue(paths.Any(path => path.StartsWith("monitoring-samples/", StringComparison.Ordinal)));
        Assert.IsTrue(paths.Any(path => path.StartsWith("snmp-metrics/", StringComparison.Ordinal)));
        Assert.IsTrue(paths.Any(path => path.StartsWith("keys/", StringComparison.Ordinal)));

        using var file = File.OpenRead(archivePath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var archiveText = new StringBuilder();
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            archiveText.Append(await reader.ReadToEndAsync());
        }
        Assert.IsFalse(archiveText.ToString().Contains("source-snmp-community-secret", StringComparison.Ordinal));
        Assert.IsFalse(archiveText.ToString().Contains("source-telegram-token-secret", StringComparison.Ordinal));
        Assert.IsFalse(archiveText.ToString().Contains("source-smtp-password-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task BackupPreviewRejectsChangedContentAndUnsafeOrMissingManifest()
    {
        using var root = new TemporaryTestDirectory();
        using var source = await BackupTestEnvironment.CreateAsync(Path.Combine(root.Path, "source"), "source");
        await source.SeedDataAsync("source device", "192.0.2.10", 45);
        var backup = await source.Service.CreateManualBackupAsync();
        Assert.IsTrue(backup.Succeeded, backup.Error);
        var archivePath = Path.Combine(source.BackupDirectory, backup.FileName!);

        RewriteEntryWithoutUpdatingManifest(archivePath, "devices.json", "{\"devices\":[]}");
        await AssertBackupRejectedAsync(() => source.Service.PreviewAsync(backup.FileName!));

        var invalidName = "uim-backup-invalid.zip";
        using (var file = File.Create(Path.Combine(source.BackupDirectory, invalidName)))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var unsafeEntry = archive.CreateEntry("../escaped.json");
            await using var stream = unsafeEntry.Open();
            await stream.WriteAsync("{}"u8.ToArray());
        }
        await AssertBackupRejectedAsync(() => source.Service.PreviewAsync(invalidName));

        var missingManifestName = "uim-backup-no-manifest.zip";
        using (var file = File.Create(Path.Combine(source.BackupDirectory, missingManifestName)))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("devices.json");
            await using var stream = entry.Open();
            await stream.WriteAsync("{}"u8.ToArray());
        }
        await AssertBackupRejectedAsync(() => source.Service.PreviewAsync(missingManifestName));
        Assert.IsFalse(File.Exists(Path.Combine(root.Path, "escaped.json")));
    }

    [TestMethod]
    public async Task FullRestoreUsesSeparateDataDirectoryAndRestoresInventoryMapsIpamHistoryAndKeys()
    {
        using var root = new TemporaryTestDirectory();
        using var source = await BackupTestEnvironment.CreateAsync(Path.Combine(root.Path, "source"), "source");
        using var destination = await BackupTestEnvironment.CreateAsync(Path.Combine(root.Path, "destination"), "destination");
        await source.SeedDataAsync("restored router", "192.0.2.10", 45);
        await destination.SeedDataAsync("old destination device", "198.51.100.20", 10);
        var backup = await source.Service.CreateManualBackupAsync();
        Assert.IsTrue(backup.Succeeded, backup.Error);
        CopyBackupTo(source, destination, backup.FileName!);

        var restore = await destination.Service.RestoreAsync(new BackupRestoreRequest(
            backup.FileName!, FullRestore: true, Modules: [], ConfirmReplace: true));

        Assert.IsTrue(restore.Succeeded, restore.Error);
        Assert.IsTrue(restore.RequiresRestart);
        Assert.AreEqual("restored router", (await destination.Documents.ReadAsync("devices.json", static () => new DeviceDocument())).Devices.Single().Name);
        Assert.AreEqual("Universal IT Monitor source", (await destination.Documents.ReadAsync("branding.json", static () => new BrandingSettings())).ProductName);
        Assert.AreEqual("restored router", (await destination.Documents.ReadAsync("network-maps.json", static () => new NetworkMapDocument())).Maps.Single().Name);
        Assert.AreEqual("192.0.2.10", (await destination.Documents.ReadAsync("ipam.json", static () => new IpamDocument())).Records.Single().IpAddress);
        Assert.AreEqual("source-admin", (await destination.Documents.ReadAsync("users.json", static () => new UserDocument())).Users.Single().UserName);
        Assert.HasCount(1, await destination.History.GetForDeviceAsync(source.DeviceId));
        var window = await destination.Samples.GetWindowAsync(source.DeviceId, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));
        Assert.HasCount(1, window.Samples);
        Assert.IsTrue((await destination.Metrics.GetManyAsync([source.DeviceId])).ContainsKey(source.DeviceId));

        using var keyProvider = destination.CreateDataProtectionProvider();
        var unprotected = keyProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("UniversalITMonitor.DeviceSecrets.v1")
            .Unprotect((await destination.Documents.ReadAsync("devices.json", static () => new DeviceDocument())).Devices.Single().Snmp.ProtectedCommunity!);
        Assert.AreEqual("source-snmp-community-secret", unprotected);
        var notificationSettings = await destination.Documents.ReadAsync("notification-settings.json", static () => new NotificationSettingsDocument());
        var notificationProtector = keyProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("UniversalITMonitor.NotificationSecrets.v1");
        Assert.AreEqual("source-telegram-token-secret", notificationProtector.Unprotect(notificationSettings.Settings.ProtectedTelegramBotToken!));
        Assert.AreEqual(45, (await destination.Documents.ReadAsync("settings.json", static () => new ApplicationSettings())).PollingIntervalSeconds);
        Assert.AreEqual(destination.BackupDirectory, (await destination.Documents.ReadAsync("backup-settings.json", static () => new BackupSettingsDocument())).Settings.BackupDirectory);

        var safetyPath = Directory.EnumerateFiles(destination.BackupDirectory, "uim-backup-safety-*.zip").Single();
        using var safetyFile = File.OpenRead(safetyPath);
        using var safetyArchive = new ZipArchive(safetyFile, ZipArchiveMode.Read);
        using var devicesReader = new StreamReader(safetyArchive.GetEntry("devices.json")!.Open());
        StringAssert.Contains(await devicesReader.ReadToEndAsync(), "old destination device");
    }

    [TestMethod]
    public async Task SelectedModuleRestorePreservesOtherModules_AndInvalidReferencesDoNotTouchLiveData()
    {
        using var root = new TemporaryTestDirectory();
        using var source = await BackupTestEnvironment.CreateAsync(Path.Combine(root.Path, "source"), "source");
        using var destination = await BackupTestEnvironment.CreateAsync(Path.Combine(root.Path, "destination"), "destination");
        await source.SeedDataAsync("source router", "192.0.2.10", 45);
        await destination.SeedDataAsync("destination router", "198.51.100.20", 10);
        var sourceBackup = await source.Service.CreateManualBackupAsync();
        Assert.IsTrue(sourceBackup.Succeeded, sourceBackup.Error);
        CopyBackupTo(source, destination, sourceBackup.FileName!);

        var settingsRestore = await destination.Service.RestoreAsync(new BackupRestoreRequest(
            sourceBackup.FileName!, FullRestore: false, Modules: [BackupModule.ApplicationSettings], ConfirmReplace: true));
        Assert.IsTrue(settingsRestore.Succeeded, settingsRestore.Error);
        Assert.IsFalse(settingsRestore.RequiresRestart);
        Assert.AreEqual("destination router", (await destination.Documents.ReadAsync("devices.json", static () => new DeviceDocument())).Devices.Single().Name);
        Assert.AreEqual(45, (await destination.Documents.ReadAsync("settings.json", static () => new ApplicationSettings())).PollingIntervalSeconds);

        // This map points at the source device ID, which is not present on the destination.
        var originalMaps = await destination.Documents.ReadAsync("network-maps.json", static () => new NetworkMapDocument());
        var failedRestore = await destination.Service.RestoreAsync(new BackupRestoreRequest(
            sourceBackup.FileName!, FullRestore: false, Modules: [BackupModule.NetworkMaps], ConfirmReplace: true));
        Assert.IsFalse(failedRestore.Succeeded);
        var afterFailure = await destination.Documents.ReadAsync("network-maps.json", static () => new NetworkMapDocument());
        Assert.AreEqual(originalMaps.Maps.Single().Id, afterFailure.Maps.Single().Id);
        Assert.AreEqual("destination router", (await destination.Documents.ReadAsync("devices.json", static () => new DeviceDocument())).Devices.Single().Name);
    }

    [TestMethod]
    public async Task RestoreRejectsIncompatibleKeyProtectionBeforeSafetyBackupOrLiveChanges()
    {
        using var root = new TemporaryTestDirectory();
        using var source = await BackupTestEnvironment.CreateAsync(Path.Combine(root.Path, "source"), "source");
        using var destination = await BackupTestEnvironment.CreateAsync(Path.Combine(root.Path, "destination"), "destination", "Certificate", "ABCDEF");
        await source.SeedDataAsync("source router", "192.0.2.10", 45);
        await destination.SeedDataAsync("destination router", "198.51.100.20", 10);
        var backup = await source.Service.CreateManualBackupAsync();
        Assert.IsTrue(backup.Succeeded, backup.Error);
        CopyBackupTo(source, destination, backup.FileName!);

        var result = await destination.Service.RestoreAsync(new BackupRestoreRequest(backup.FileName!, true, [], true));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("destination router", (await destination.Documents.ReadAsync("devices.json", static () => new DeviceDocument())).Devices.Single().Name);
        Assert.IsFalse(Directory.EnumerateFiles(destination.BackupDirectory, "uim-backup-safety-*.zip").Any());
    }

    [TestMethod]
    public async Task AutomaticBackupRunsWhenDueAndRetentionKeepsSafetyBackups()
    {
        using var root = new TemporaryTestDirectory();
        using var source = await BackupTestEnvironment.CreateAsync(Path.Combine(root.Path, "source"), "source");
        using var destination = await BackupTestEnvironment.CreateAsync(Path.Combine(root.Path, "destination"), "destination");
        await source.SeedDataAsync("source router", "192.0.2.10", 45);
        await destination.SeedDataAsync("destination router", "198.51.100.20", 10);
        var saved = await source.Service.SaveSettingsAsync(new BackupSettings
        {
            AutomaticEnabled = true,
            IntervalHours = 1,
            RetentionCount = 1,
            BackupDirectory = source.BackupDirectory
        });
        Assert.IsTrue(saved.Succeeded, saved.Error);
        await source.Documents.WriteAsync("backup-settings.json", new BackupSettingsDocument
        {
            Settings = new BackupSettings
            {
                AutomaticEnabled = true,
                IntervalHours = 1,
                RetentionCount = 1,
                BackupDirectory = source.BackupDirectory,
                NextAutomaticUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        });

        var autoResult = await source.Service.CreateDueAutomaticBackupAsync();
        Assert.IsTrue(autoResult.Succeeded, autoResult.Error);
        var sourceBackup = await source.Service.CreateManualBackupAsync();
        Assert.IsTrue(sourceBackup.Succeeded, sourceBackup.Error);
        CopyBackupTo(source, destination, sourceBackup.FileName!);
        var restore = await destination.Service.RestoreAsync(new BackupRestoreRequest(sourceBackup.FileName!, true, [], true));
        Assert.IsTrue(restore.Succeeded, restore.Error);
        Assert.IsTrue(Directory.EnumerateFiles(destination.BackupDirectory, "uim-backup-safety-*.zip").Any());
        await source.Service.CreateManualBackupAsync();
        await source.Service.CreateManualBackupAsync();

        var files = await source.Service.GetBackupFilesAsync();
        Assert.AreEqual(1, files.Count(file => !file.IsSafetyBackup));
        Assert.IsTrue(Directory.EnumerateFiles(destination.BackupDirectory, "uim-backup-safety-*.zip").Any());
    }

    private static void RewriteEntryWithoutUpdatingManifest(string archivePath, string entryName, string replacement)
    {
        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(file, ZipArchiveMode.Update);
        archive.GetEntry(entryName)!.Delete();
        using var writer = new StreamWriter(archive.CreateEntry(entryName).Open());
        writer.Write(replacement);
    }

    private static async Task AssertBackupRejectedAsync(Func<Task> action)
    {
        try
        {
            await action();
            Assert.Fail("Expected the backup archive to be rejected.");
        }
        catch (BackupArchiveException)
        {
        }
    }

    private static void CopyBackupTo(BackupTestEnvironment source, BackupTestEnvironment destination, string fileName)
    {
        File.Copy(Path.Combine(source.BackupDirectory, fileName), Path.Combine(destination.BackupDirectory, fileName), overwrite: true);
    }
}

internal sealed class BackupTestEnvironment : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _dataProtectionServices;

    private BackupTestEnvironment(string root, string name, string keyMode, string? thumbprint)
    {
        _root = root;
        DataDirectory = Path.Combine(root, "data");
        WebRoot = Path.Combine(root, "web");
        BackupDirectory = Path.Combine(root, "backups");
        Directory.CreateDirectory(WebRoot);
        Documents = new FileJsonDocumentStore(DataDirectory);
        History = new JsonMonitoringHistoryStore(DataDirectory);
        Samples = new JsonMonitoringSampleStore(DataDirectory);
        Metrics = new JsonSnmpMetricsStore(DataDirectory);
        _dataProtectionServices = CreateDataProtectionProvider();
        var options = new BackupKeyProtectionOptions(keyMode, thumbprint);
        Service = new BackupService(DataDirectory, WebRoot, options, Documents, new ApplicationMaintenanceLock(), NullLogger<BackupService>.Instance);
        Name = name;
    }

    public string Name { get; }
    public string DataDirectory { get; }
    public string WebRoot { get; }
    public string BackupDirectory { get; }
    public FileJsonDocumentStore Documents { get; }
    public JsonMonitoringHistoryStore History { get; }
    public JsonMonitoringSampleStore Samples { get; }
    public JsonSnmpMetricsStore Metrics { get; }
    public BackupService Service { get; }
    public Guid DeviceId { get; private set; }

    public static async Task<BackupTestEnvironment> CreateAsync(string root, string name, string keyMode = "Default", string? thumbprint = null)
    {
        var environment = new BackupTestEnvironment(root, name, keyMode, thumbprint);
        await new InitialDataSeeder(environment.Documents).InitializeAsync();
        await environment.Documents.WriteAsync("users.json", new UserDocument
        {
            Users = [new LocalUser { Id = name + "-admin", UserName = name + "-admin", DisplayName = "Test Administrator", PasswordHash = "test-hash", Roles = [SystemRoles.Administrator] }]
        });
        await environment.Documents.WriteAsync("roles.json", new RoleDocument
        {
            Roles = [new RoleDefinition { Name = SystemRoles.Administrator }]
        });
        var backupSettings = await environment.Service.SaveSettingsAsync(new BackupSettings
        {
            BackupDirectory = environment.BackupDirectory
        });
        if (!backupSettings.Succeeded) throw new InvalidOperationException(backupSettings.Error);
        return environment;
    }

    public async Task SeedDataAsync(string deviceName, string ipAddress, int pollingIntervalSeconds)
    {
        var location = new ManagedLocation { Id = Guid.NewGuid(), Name = Name + " site" };
        var building = new ManagedLocation { Id = Guid.NewGuid(), Name = Name + " building", Kind = LocationKind.Building, ParentLocationId = location.Id };
        var group = new ManagedGroup { Id = Guid.NewGuid(), Name = Name + " routers" };
        var deviceId = Guid.NewGuid();
        var protector = _dataProtectionServices.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("UniversalITMonitor.DeviceSecrets.v1");
        var notificationProtector = _dataProtectionServices.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("UniversalITMonitor.NotificationSecrets.v1");
        var device = new NetworkDevice
        {
            Id = deviceId,
            Name = deviceName,
            IpAddress = ipAddress,
            Type = DeviceType.Router,
            Location = location.Name,
            LocationId = location.Id,
            Building = building.Name,
            BuildingId = building.Id,
            Group = group.Name,
            GroupId = group.Id,
            Status = DeviceStatus.Up,
            ResponseTimeMilliseconds = 12,
            LastCheckUtc = DateTimeOffset.UtcNow,
            Snmp = new SnmpConfiguration { ProtectedCommunity = protector.Protect(Name + "-snmp-community-secret") }
        };
        var subnet = new IpamSubnet
        {
            Id = Guid.NewGuid(), Cidr = "192.0.2.0/24", NetworkAddress = "192.0.2.0", PrefixLength = 24,
            LocationId = location.Id, BuildingId = building.Id, Location = location.Name, Building = building.Name
        };
        var map = new NetworkMap
        {
            Id = Guid.NewGuid(), Name = deviceName, LocationId = location.Id, BuildingId = building.Id,
            Nodes = [new NetworkMapNode { DeviceId = deviceId }]
        };
        await Documents.WriteAsync("inventory-structure.json", new InventoryStructureDocument { Locations = [location, building], Groups = [group] });
        await Documents.WriteAsync("devices.json", new DeviceDocument { Devices = [device] });
        await Documents.WriteAsync("network-maps.json", new NetworkMapDocument { Maps = [map] });
        await Documents.WriteAsync("ipam.json", new IpamDocument
        {
            Subnets = [subnet],
            Records = [new IpamRecord { Id = Guid.NewGuid(), DeviceId = deviceId, IpAddress = ipAddress, SubnetId = subnet.Id,
                DeviceName = deviceName, LocationId = location.Id, BuildingId = building.Id, GroupId = group.Id,
                AssignmentStatus = IpAssignmentStatus.Assigned }]
        });
        await Documents.WriteAsync("settings.json", new ApplicationSettings { PollingIntervalSeconds = pollingIntervalSeconds });
        await Documents.WriteAsync("branding.json", new BrandingSettings { ProductName = "Universal IT Monitor " + Name });
        await Documents.WriteAsync("notification-settings.json", new NotificationSettingsDocument
        {
            Settings = new NotificationSettings
            {
                ProtectedTelegramBotToken = notificationProtector.Protect(Name + "-telegram-token-secret"),
                ProtectedSmtpUserName = notificationProtector.Protect(Name + "-smtp-user-secret"),
                ProtectedSmtpPassword = notificationProtector.Protect(Name + "-smtp-password-secret")
            }
        });
        await History.AppendAsync(new MonitoringHistoryEntry
        {
            DeviceId = deviceId, OccurredUtc = DateTimeOffset.UtcNow.AddMinutes(-2), PreviousStatus = DeviceStatus.Down,
            Status = DeviceStatus.Up, ResponseTimeMilliseconds = 12
        });
        await Samples.AppendAsync(new MonitoringCheckSample
        {
            DeviceId = deviceId, CheckedUtc = DateTimeOffset.UtcNow.AddMinutes(-1), Status = DeviceStatus.Up, ResponseTimeMilliseconds = 12
        });
        await Metrics.WriteAsync(deviceId, new SnmpMetrics { CollectedUtc = DateTimeOffset.UtcNow, SystemName = deviceName });
        await Documents.WriteAsync("themes.json", new ThemeSettings { Name = "Dark" });
        DeviceId = deviceId;
    }

    public ServiceProvider CreateDataProtectionProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(DataDirectory, "keys")))
            .SetApplicationName("UniversalITMonitor");
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _dataProtectionServices.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
