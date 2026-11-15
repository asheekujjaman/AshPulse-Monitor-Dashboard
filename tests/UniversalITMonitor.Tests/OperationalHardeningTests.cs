using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalITMonitor.Application.Accounts;
using UniversalITMonitor.Application.Backups;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Application.Presentation;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Storage.Json;
using UniversalITMonitor.Web.Infrastructure;
using UniversalITMonitor.Web.Logging;

namespace UniversalITMonitor.Tests;

[TestClass]
public sealed class OperationalHardeningTests
{
    [TestMethod]
    public async Task MissingPrimaryJsonRecoversFromValidBakAndInvalidBakIsPreserved()
    {
        using var directory = new TemporaryTestDirectory();
        var store = new FileJsonDocumentStore(directory.Path);
        var primary = Path.Combine(directory.Path, "settings.json");
        await store.WriteAsync("settings.json", new ApplicationSettings { PollingIntervalSeconds = 77 });
        File.Copy(primary, primary + ".bak");
        File.Delete(primary);

        var recovered = await store.ReadAsync("settings.json", static () => new ApplicationSettings());
        Assert.AreEqual(77, recovered.PollingIntervalSeconds);
        Assert.IsTrue(File.Exists(primary));

        File.Delete(primary);
        await File.WriteAllTextAsync(primary + ".bak", "{ invalid");
        await ExpectThrowsAsync<InvalidDataException>(async () =>
            await store.EnsureAsync("settings.json", new ApplicationSettings()));
        Assert.IsFalse(File.Exists(primary));
        StringAssert.Contains(await File.ReadAllTextAsync(primary + ".bak"), "invalid");
    }

    [TestMethod]
    public void StorageDefaultsToAppDataAndMustStayOutsideWebRoot()
    {
        using var directory = new TemporaryTestDirectory();
        var application = Path.Combine(directory.Path, "published");
        var webRoot = Path.Combine(application, "wwwroot");
        var external = Path.Combine(directory.Path, "service-data");
        Directory.CreateDirectory(webRoot);

        Assert.AreEqual(Path.GetFullPath(Path.Combine(application, "App_Data")),
            StoragePathResolver.ResolveDataDirectory(application, webRoot, null));
        Assert.AreEqual(Path.GetFullPath(Path.Combine(application, "App_Data")),
            StoragePathResolver.ResolveDataDirectory(application, webRoot, "App_Data"));
        Assert.AreEqual(Path.GetFullPath(Path.Combine(application, "App_Data", "Custom")),
            StoragePathResolver.ResolveDataDirectory(application, webRoot, Path.Combine(application, "App_Data", "Custom")));
        ExpectThrows<InvalidOperationException>(() =>
            StoragePathResolver.ResolveDataDirectory(application, webRoot, webRoot));
        Assert.AreEqual(Path.GetFullPath(external),
            StoragePathResolver.ResolveDataDirectory(application, webRoot, external));
    }

    [TestMethod]
    public async Task ProductionBackupDirectoryMustBeExternalToPublishedApplication()
    {
        using var directory = new TemporaryTestDirectory();
        var application = Path.Combine(directory.Path, "published");
        var webRoot = Path.Combine(application, "wwwroot");
        var data = Path.Combine(directory.Path, "service-data");
        Directory.CreateDirectory(webRoot);
        var store = new FileJsonDocumentStore(data);
        var service = new BackupService(data, webRoot, new BackupKeyProtectionOptions("Default"), store,
            new ApplicationMaintenanceLock(), NullLogger<BackupService>.Instance, application, requireExternalDirectories: true);

        var rejected = await service.SaveSettingsAsync(new BackupSettings { BackupDirectory = Path.Combine(application, "backups") });
        Assert.IsFalse(rejected.Succeeded);
        var accepted = await service.SaveSettingsAsync(new BackupSettings { BackupDirectory = Path.Combine(directory.Path, "external-backups") });
        Assert.IsTrue(accepted.Succeeded, accepted.Error);
        Assert.IsTrue(Directory.Exists(Path.Combine(directory.Path, "external-backups")));
    }

    [TestMethod]
    public async Task InstanceLeasePreventsOverlappingApplicationProcesses()
    {
        using var directory = new TemporaryTestDirectory();
        var dataDirectory = Path.Combine(directory.Path, "data");
        Directory.CreateDirectory(dataDirectory);
        var first = await ApplicationInstanceLease.AcquireAsync(dataDirectory);
        try
        {
            var secondTask = ApplicationInstanceLease.AcquireAsync(dataDirectory);
            await Task.Delay(250);
            Assert.IsFalse(secondTask.IsCompleted, "A second process acquired the same data-directory lease.");
            await first.DisposeAsync();
            await using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await first.DisposeAsync();
        }
    }

    [TestMethod]
    public void RollingLoggerRedactsCredentialsAndRotatesFiles()
    {
        using var directory = new TemporaryTestDirectory();
        var logDirectory = Path.Combine(directory.Path, "logs");
        using (var provider = new RollingRedactedFileLoggerProvider(logDirectory, 64 * 1024, retainedFiles: 3))
        using (var factory = LoggerFactory.Create(builder => builder.AddProvider(provider).SetMinimumLevel(LogLevel.Information)))
        {
            var logger = factory.CreateLogger("HardeningTest");
            logger.LogWarning("Credentials {Password} {TelegramToken} {SnmpCommunity}",
                "smtp-password-secret", "bot123:abcdefghijklmnopqrstuvwxyz-ABCDEFGH", "snmp-community-secret");
            for (var index = 0; index < 12; index++)
            {
                logger.LogInformation("Rotation sample {Index} {Payload}", index, new string('x', 10_000));
            }
        }

        var files = Directory.GetFiles(logDirectory, "uim-*.jsonl");
        Assert.AreEqual(files.Length, Math.Max(2, files.Length), "Log size threshold should create more than one file.");
        Assert.AreEqual(files.Length, Math.Min(3, files.Length), "Configured log retention must be enforced.");
        var content = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        Assert.IsFalse(content.Contains("smtp-password-secret", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("bot123:abcdefghijklmnopqrstuvwxyz-ABCDEFGH", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("snmp-community-secret", StringComparison.Ordinal));
        StringAssert.Contains(content, "[REDACTED]");
    }

    [TestMethod]
    public async Task ThemeDefaultsPersistAcrossRequestsWithoutChangingBranding()
    {
        using var directory = new TemporaryTestDirectory();
        var store = new FileJsonDocumentStore(directory.Path);
        var branding = new BrandingSettings { ProductName = "Operations Monitor", OrganizationName = "Example Network", LogoPath = "logo.svg" };
        await store.WriteAsync("branding.json", branding);
        var service = new AppPresentationService(store);

        foreach (var preset in ThemePresetCatalog.Names)
        {
            Assert.IsTrue(await service.SetDefaultThemeAsync(preset));
            var presentation = await service.GetAsync();
            Assert.AreEqual(preset, presentation.Theme.Name);
            Assert.AreEqual(ThemePresetCatalog.Create(preset).PrimaryColor, presentation.Theme.PrimaryColor);
            Assert.AreEqual(branding.ProductName, presentation.Branding.ProductName);
            Assert.AreEqual(branding.OrganizationName, presentation.Branding.OrganizationName);
            Assert.AreEqual(branding.LogoPath, presentation.Branding.LogoPath);
        }

        Assert.IsFalse(await service.SetDefaultThemeAsync("untrusted-css-value"));
    }

    [TestMethod]
    public async Task FailedLoginLockoutIsPersistedAndBlocksCorrectPasswordUntilExpiry()
    {
        using var directory = new TemporaryTestDirectory();
        var store = new FileJsonDocumentStore(directory.Path);
        var users = new JsonUserRepository(store);
        var roles = new JsonRoleRepository(store);
        await store.WriteAsync("roles.json", new RoleDocument { Roles = [new RoleDefinition { Name = SystemRoles.Administrator }] });
        var hasher = new PasswordHasher<LocalUser>();
        var admin = new LocalUser { Id = "admin", UserName = "admin", DisplayName = "Administrator", Roles = [SystemRoles.Administrator] };
        admin.PasswordHash = hasher.HashPassword(admin, "StrongPassword!2026");
        await store.WriteAsync("users.json", new UserDocument { Users = [admin] });
        var accounts = new AccountService(users, roles, hasher);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.IsNull(await accounts.AuthenticateAsync("admin", "incorrect-password"));
        }

        var locked = await users.FindByIdAsync("admin");
        Assert.IsNotNull(locked);
        Assert.AreEqual(5, locked.AccessFailedCount);
        Assert.IsTrue(locked.LockoutEndUtc > DateTimeOffset.UtcNow);
        Assert.IsNull(await accounts.AuthenticateAsync("admin", "StrongPassword!2026"));

        locked.LockoutEndUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await store.WriteAsync("users.json", new UserDocument { Users = [locked] });
        Assert.IsNotNull(await accounts.AuthenticateAsync("admin", "StrongPassword!2026"));
        var reset = await users.FindByIdAsync("admin");
        Assert.IsNotNull(reset);
        Assert.AreEqual(0, reset.AccessFailedCount);
        Assert.IsNull(reset.LockoutEndUtc);
    }

    [TestMethod]
    public async Task ReferenceReconciliationRepairsStableLinksAndRetainsUnresolvedMapData()
    {
        using var directory = new TemporaryTestDirectory();
        var store = new FileJsonDocumentStore(directory.Path);
        var siteId = Guid.NewGuid();
        var staleLocationId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var staleDeviceId = Guid.NewGuid();
        var danglingMapDeviceId = Guid.NewGuid();
        var site = new ManagedLocation { Id = siteId, Name = "Dhaka", Kind = LocationKind.Site };
        var device = new NetworkDevice
        {
            Id = deviceId,
            Name = "Core Router",
            IpAddress = "192.0.2.10",
            Location = "Dhaka",
            LocationId = staleLocationId,
            Group = "Network Devices"
        };
        var ipRecord = new IpamRecord
        {
            Id = Guid.NewGuid(),
            DeviceId = staleDeviceId,
            IpAddress = "192.0.2.10",
            Location = "Dhaka",
            LocationId = staleLocationId,
            AssignmentStatus = IpAssignmentStatus.Assigned
        };
        var map = new NetworkMap
        {
            Id = Guid.NewGuid(),
            Name = "Core",
            LocationId = staleLocationId,
            Nodes = [new NetworkMapNode { DeviceId = danglingMapDeviceId }]
        };
        await store.WriteAsync("devices.json", new DeviceDocument { Devices = [device] });
        await store.WriteAsync("inventory-structure.json", new InventoryStructureDocument
        {
            Locations = [site, new ManagedLocation { Id = siteId, Name = "Duplicate stable ID", Kind = LocationKind.Site }]
        });
        await store.WriteAsync("ipam.json", new IpamDocument { Records = [ipRecord] });
        await store.WriteAsync("network-maps.json", new NetworkMapDocument { Maps = [map] });
        var reconciler = new InventoryReferenceReconciler(store, NullLogger<InventoryReferenceReconciler>.Instance);

        var report = await reconciler.ReconcileAsync();
        var repairedDevice = (await store.ReadAsync("devices.json", static () => new DeviceDocument())).Devices.Single();
        var repairedIpRecord = (await store.ReadAsync("ipam.json", static () => new IpamDocument())).Records.Single();
        var repairedMap = (await store.ReadAsync("network-maps.json", static () => new NetworkMapDocument())).Maps.Single();
        var repairedHierarchy = await store.ReadAsync("inventory-structure.json", static () => new InventoryStructureDocument());
        Assert.IsNull(repairedDevice.LocationId);
        Assert.AreEqual("Dhaka", repairedDevice.Location);
        Assert.AreEqual(deviceId, repairedIpRecord.DeviceId, "A unique matching IP should reattach an IP record to its stable device ID.");
        Assert.IsNull(repairedIpRecord.LocationId);
        Assert.AreEqual("Dhaka", repairedIpRecord.Location);
        Assert.AreEqual(2, repairedHierarchy.Locations.Select(location => location.Id).Distinct().Count());
        Assert.IsTrue(report.Issues.Any(issue => issue.EntityType == "Location" && issue.Field == "Id" && issue.Repaired));
        Assert.IsNull(repairedMap.LocationId);
        Assert.AreEqual(danglingMapDeviceId, repairedMap.Nodes.Single().DeviceId, "Unresolved map nodes must be retained for admin review.");
        Assert.IsTrue(report.Issues.Any(issue => issue.EntityType == "Network map" && !issue.Repaired));
        Assert.IsTrue(report.Issues.Any(issue => issue.EntityType == "IP record" && issue.Repaired));
    }

    private static void ExpectThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        Assert.Fail($"Expected {typeof(TException).Name}.");
    }

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException) { return; }
        Assert.Fail($"Expected {typeof(TException).Name}.");
    }
}
