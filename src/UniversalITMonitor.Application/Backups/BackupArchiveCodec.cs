using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Notifications;
using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Application.Backups;

internal sealed class BackupArchiveCodec(
    BackupKeyProtectionOptions keyOptions,
    ILogger logger)
{
    private const string ManifestPath = "manifest.json";
    private const int MaximumEntryCount = 20000;
    private const long MaximumArchiveSize = 20L * 1024 * 1024 * 1024;
    private const long MaximumExpandedSize = 40L * 1024 * 1024 * 1024;
    private const long MaximumEntrySize = 8L * 1024 * 1024 * 1024;
    private const long MaximumManifestSize = 8L * 1024 * 1024;
    private static readonly string[] RequiredFiles =
    [
        "users.json",
        "roles.json",
        "settings.json",
        "devices.json",
        "inventory-structure.json",
        "network-maps.json",
        "ipam.json",
        "branding.json",
        "themes.json"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task CreateAsync(string dataDirectory, string destinationPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var files = EnumeratePersistentFiles(dataDirectory)
            .Select(path => (FullPath: path, RelativePath: ToArchivePath(Path.GetRelativePath(dataDirectory, path))))
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (files.Any(item => string.Equals(item.RelativePath, ManifestPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BackupArchiveException("The data directory contains a reserved backup manifest path.");
        }

        var manifest = new BackupManifest
        {
            ApplicationVersion = GetApplicationVersion(),
            CreatedUtc = DateTimeOffset.UtcNow,
            KeyProtection = new BackupKeyProtection
            {
                Mode = keyOptions.Mode,
                CertificateThumbprint = keyOptions.CertificateThumbprint
            }
        };
        var temporaryPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = archive.CreateEntry(file.RelativePath, CompressionLevel.Optimal);
                        await using var input = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await using var output = entry.Open();
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        var length = await CopyAndHashAsync(input, output, hash, cancellationToken);
                        manifest.Files.Add(new BackupManifestFile
                        {
                            Path = file.RelativePath,
                            Length = length,
                            Sha256 = Convert.ToHexString(hash.GetHashAndReset())
                        });
                    }

                    var manifestEntry = archive.CreateEntry(ManifestPath, CompressionLevel.Optimal);
                    await using var manifestStream = manifestEntry.Open();
                    await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
                }

                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            await ValidateAsync(temporaryPath, Path.GetFileName(temporaryPath), cancellationToken);
            File.Move(temporaryPath, destinationPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public async Task<ValidatedBackup> ValidateAsync(string archivePath, string displayName, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(archivePath);
        if (!fileInfo.Exists || fileInfo.Length <= 0 || fileInfo.Length > MaximumArchiveSize)
        {
            throw new BackupArchiveException("The backup file is missing, empty, or exceeds the supported size limit.");
        }

        var temporaryRoot = Path.Combine(Path.GetTempPath(), "uim-backup-validate-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporaryRoot);
            BackupManifest manifest;
            Dictionary<string, BackupManifestFile> manifestFiles;
            using (var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false))
            {
                if (archive.Entries.Count is 0 or > MaximumEntryCount)
                {
                    throw new BackupArchiveException("The backup archive has an unsupported number of entries.");
                }

                var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    ValidateArchivePath(entry);
                    if (!entries.TryAdd(entry.FullName, entry))
                    {
                        throw new BackupArchiveException("The backup archive contains duplicate file paths.");
                    }
                }

                if (!entries.TryGetValue(ManifestPath, out var manifestEntry))
                {
                    throw new BackupArchiveException("The backup manifest is missing.");
                }
                if (manifestEntry.Length <= 0 || manifestEntry.Length > MaximumManifestSize)
                {
                    throw new BackupArchiveException("The backup manifest exceeds the supported size limit.");
                }

                try
                {
                    await using var manifestStream = manifestEntry.Open();
                    manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, JsonOptions, cancellationToken)
                        ?? throw new JsonException("Null manifest.");
                }
                catch (JsonException)
                {
                    throw new BackupArchiveException("The backup manifest is invalid.");
                }

                ValidateManifestHeader(manifest);
                if (manifest.Files is null)
                    throw new BackupArchiveException("The backup manifest has no file list.");

                manifestFiles = new Dictionary<string, BackupManifestFile>(StringComparer.OrdinalIgnoreCase);
                long totalLength = 0;
                foreach (var item in manifest.Files)
                {
                    ValidateManifestPath(item.Path);
                    if (item.Length < 0 || item.Length > MaximumEntrySize ||
                        string.IsNullOrWhiteSpace(item.Sha256) || item.Sha256.Length != 64 || !item.Sha256.All(Uri.IsHexDigit) ||
                        !manifestFiles.TryAdd(item.Path, item))
                    {
                        throw new BackupArchiveException("The backup manifest contains an invalid file entry.");
                    }
                    totalLength = checked(totalLength + item.Length);
                }

                if (totalLength > MaximumExpandedSize || entries.Count != manifestFiles.Count + 1 ||
                    entries.Keys.Any(path => !string.Equals(path, ManifestPath, StringComparison.OrdinalIgnoreCase) && !manifestFiles.ContainsKey(path)) ||
                    manifestFiles.Keys.Any(path => !entries.ContainsKey(path)))
                {
                    throw new BackupArchiveException("The archive contents do not match the manifest file list.");
                }

                foreach (var item in manifest.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = entries[item.Path];
                    if (entry.Length != item.Length)
                    {
                        throw new BackupArchiveException("A backup file length does not match its manifest entry.");
                    }

                    await using var entryStream = entry.Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var length = await CopyAndHashAsync(entryStream, Stream.Null, hash, cancellationToken);
                    if (length != item.Length || !CryptographicOperations.FixedTimeEquals(
                            Convert.FromHexString(item.Sha256), hash.GetHashAndReset()))
                    {
                        throw new BackupArchiveException("A backup file failed its integrity check.");
                    }
                }

                foreach (var required in RequiredFiles)
                {
                    if (!manifestFiles.ContainsKey(required))
                    {
                        throw new BackupArchiveException("The backup is missing one or more required application data files.");
                    }
                }

                await ExtractEntriesAsync(archive, temporaryRoot, manifestFiles, null, cancellationToken);
            }

            await ValidateDataDirectoryAsync(temporaryRoot, manifest.KeyProtection, cancellationToken);
            var preview = new BackupPreview(
                displayName,
                manifest.ApplicationVersion,
                manifest.CreatedUtc,
                manifest.KeyProtection,
                manifest.Files.Sum(item => item.Length),
                manifest.Files.OrderBy(item => item.Path, StringComparer.Ordinal)
                    .Select(item => new BackupFileSummary(item.Path, item.Length, item.Sha256)).ToArray(),
                GetAvailableModules(manifest.Files.Select(item => item.Path)));
            return new ValidatedBackup(manifest, manifestFiles, preview);
        }
        catch (BackupArchiveException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("Backup validation failed ({FailureType}).", exception.GetType().Name);
            throw new BackupArchiveException("The backup archive is invalid or cannot be read.");
        }
        finally
        {
            TryDeleteDirectory(temporaryRoot);
        }
    }

    public async Task ExtractAllAsync(string archivePath, string destinationRoot, ValidatedBackup backup, CancellationToken cancellationToken)
    {
        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        await ExtractEntriesAsync(archive, destinationRoot, backup.Files, null, cancellationToken);
    }

    public async Task ExtractModulesAsync(
        string archivePath,
        string destinationRoot,
        ValidatedBackup backup,
        IReadOnlyCollection<BackupModule> modules,
        CancellationToken cancellationToken)
    {
        var selectedPaths = backup.Files.Keys.Where(path => modules.Any(module => BackupModulePaths.Owns(module, path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        await ExtractEntriesAsync(archive, destinationRoot, backup.Files, selectedPaths, cancellationToken);
    }

    public async Task ValidateDataDirectoryAsync(
        string root,
        BackupKeyProtection? archiveKeyProtection,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var required in RequiredFiles)
            {
                if (!File.Exists(Path.Combine(root, ToPlatformPath(required))))
                    throw new BackupArchiveException("The backup is missing one or more required application data files.");
            }

            var users = await ReadJsonAsync<UserDocument>(root, "users.json", cancellationToken);
            var devices = await ReadJsonAsync<DeviceDocument>(root, "devices.json", cancellationToken);
            var structure = await ReadJsonAsync<InventoryStructureDocument>(root, "inventory-structure.json", cancellationToken);
            var maps = await ReadJsonAsync<NetworkMapDocument>(root, "network-maps.json", cancellationToken);
            var ipam = await ReadJsonAsync<IpamDocument>(root, "ipam.json", cancellationToken);
            var appSettings = await ReadJsonAsync<ApplicationSettings>(root, "settings.json", cancellationToken);
            _ = await ReadJsonAsync<RoleDocument>(root, "roles.json", cancellationToken);
            _ = await ReadJsonAsync<BrandingSettings>(root, "branding.json", cancellationToken);
            _ = await ReadJsonAsync<ThemeSettings>(root, "themes.json", cancellationToken);
            if (File.Exists(Path.Combine(root, "notification-settings.json")) &&
                (await ReadJsonAsync<NotificationSettingsDocument>(root, "notification-settings.json", cancellationToken)).Settings is null)
                throw new BackupArchiveException("The backup contains invalid notification settings.");
            if (File.Exists(Path.Combine(root, "notification-history.json")) &&
                (await ReadJsonAsync<NotificationHistoryDocument>(root, "notification-history.json", cancellationToken)).Records is null)
                throw new BackupArchiveException("The backup contains invalid notification history.");
            var backupSettingsPath = Path.Combine(root, "backup-settings.json");
            if (File.Exists(backupSettingsPath))
            {
                var backupSettings = (await ReadJsonAsync<BackupSettingsDocument>(root, "backup-settings.json", cancellationToken)).Settings;
                if (backupSettings is null)
                    throw new BackupArchiveException("The backup contains invalid backup settings.");
                if (backupSettings.IntervalHours is < 1 or > 168 || backupSettings.RetentionCount is < 1 or > 365)
                    throw new BackupArchiveException("The backup contains out-of-range automatic backup settings.");
            }
            if (File.Exists(Path.Combine(root, "backup-history.json")) &&
                (await ReadJsonAsync<BackupHistoryDocument>(root, "backup-history.json", cancellationToken)).Records is null)
                throw new BackupArchiveException("The backup contains invalid backup history.");

            ValidateDataRelationships(users, devices, structure, maps, ipam, appSettings);
            await ValidateEncryptedCredentialsAsync(root, devices, archiveKeyProtection, cancellationToken);
        }
        catch (BackupArchiveException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("Backup data validation failed ({FailureType}).", exception.GetType().Name);
            throw new BackupArchiveException("One or more backup data files are invalid or incompatible.");
        }
    }

    public static JsonSerializerOptions SerializerOptions => JsonOptions;

    private async Task ValidateEncryptedCredentialsAsync(
        string root,
        DeviceDocument devices,
        BackupKeyProtection? archiveKeyProtection,
        CancellationToken cancellationToken)
    {
        if (devices.Devices is null || devices.Devices.Any(device => device is null || device.Snmp is null))
            throw new BackupArchiveException("The backup contains an invalid device document.");

        var secrets = devices.Devices.SelectMany(device => new[]
            {
                device.Snmp.ProtectedCommunity,
                device.Snmp.ProtectedAuthenticationPassword,
                device.Snmp.ProtectedPrivacyPassword
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();

        var notificationPath = Path.Combine(root, "notification-settings.json");
        if (File.Exists(notificationPath))
        {
            var notificationSettings = await ReadJsonAsync<NotificationSettingsDocument>(root, "notification-settings.json", cancellationToken);
            if (notificationSettings.Settings is null)
                throw new BackupArchiveException("The backup contains invalid notification settings.");
            secrets.AddRange(new[]
            {
                notificationSettings.Settings.ProtectedTelegramBotToken,
                notificationSettings.Settings.ProtectedSmtpUserName,
                notificationSettings.Settings.ProtectedSmtpPassword
            }.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>());
        }

        var keyFiles = EnumeratePersistentFiles(Path.Combine(root, "keys")).ToArray();
        if (keyFiles.Length > 0 && archiveKeyProtection is not null && !IsCompatibleKeyProtection(archiveKeyProtection))
        {
            throw new BackupArchiveException("The backup Data Protection key configuration is incompatible with this server. No live data was changed.");
        }

        if (secrets.Count == 0) return;
        if (keyFiles.Length == 0 || archiveKeyProtection is null || !IsCompatibleKeyProtection(archiveKeyProtection))
        {
            throw new BackupArchiveException("Encrypted credentials cannot be validated because the compatible Data Protection keys are missing. No live data was changed.");
        }

        try
        {
            using var provider = CreateKeyRingProvider(Path.Combine(root, "keys"));
            var deviceProtector = provider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()
                .CreateProtector("UniversalITMonitor.DeviceSecrets.v1");
            var notificationProtector = provider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()
                .CreateProtector("UniversalITMonitor.NotificationSecrets.v1");
            foreach (var device in devices.Devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UnprotectIfPresent(deviceProtector, device.Snmp.ProtectedCommunity);
                UnprotectIfPresent(deviceProtector, device.Snmp.ProtectedAuthenticationPassword);
                UnprotectIfPresent(deviceProtector, device.Snmp.ProtectedPrivacyPassword);
            }

            if (File.Exists(notificationPath))
            {
                var notificationSettings = await ReadJsonAsync<NotificationSettingsDocument>(root, "notification-settings.json", cancellationToken);
                UnprotectIfPresent(notificationProtector, notificationSettings.Settings.ProtectedTelegramBotToken);
                UnprotectIfPresent(notificationProtector, notificationSettings.Settings.ProtectedSmtpUserName);
                UnprotectIfPresent(notificationProtector, notificationSettings.Settings.ProtectedSmtpPassword);
            }
        }
        catch (BackupArchiveException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("Backup credential validation failed ({FailureType}).", exception.GetType().Name);
            throw new BackupArchiveException("One or more encrypted credentials cannot be opened with the included Data Protection keys. No live data was changed.");
        }
    }

    private bool IsCompatibleKeyProtection(BackupKeyProtection archiveKeyProtection)
    {
        if (!string.Equals(archiveKeyProtection.Mode, keyOptions.Mode, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(keyOptions.Mode, "Certificate", StringComparison.OrdinalIgnoreCase))
        {
            var archiveThumbprint = NormalizeThumbprint(archiveKeyProtection.CertificateThumbprint);
            var configuredThumbprint = NormalizeThumbprint(keyOptions.CertificateThumbprint);
            return archiveThumbprint is not null && configuredThumbprint is not null &&
                   string.Equals(archiveThumbprint, configuredThumbprint, StringComparison.OrdinalIgnoreCase);
        }
        return string.IsNullOrWhiteSpace(archiveKeyProtection.CertificateThumbprint);
    }

    private ServiceProvider CreateKeyRingProvider(string keyDirectory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var dataProtection = services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
            .SetApplicationName("UniversalITMonitor");
        if (keyOptions.Certificate is not null)
        {
            dataProtection.ProtectKeysWithCertificate(keyOptions.Certificate);
        }
        else if (keyOptions.UseWindowsDpapi && OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: false);
        }

        return services.BuildServiceProvider();
    }

    private static void UnprotectIfPresent(Microsoft.AspNetCore.DataProtection.IDataProtector protector, string? protectedValue)
    {
        if (!string.IsNullOrWhiteSpace(protectedValue)) _ = protector.Unprotect(protectedValue);
    }

    private static void ValidateDataRelationships(
        UserDocument users,
        DeviceDocument devices,
        InventoryStructureDocument structure,
        NetworkMapDocument maps,
        IpamDocument ipam,
        ApplicationSettings settings)
    {
        if (users.Users is null || users.Users.Any(user => user is null || user.Roles is null) ||
            devices.Devices is null || structure.Locations is null || structure.Groups is null ||
            structure.Locations.Any(location => location is null) || structure.Groups.Any(group => group is null) ||
            maps.Maps is null || maps.Maps.Any(map => map is null || map.Nodes is null || map.Connections is null ||
                map.Nodes.Any(node => node is null) || map.Connections.Any(connection => connection is null)) ||
            ipam.Records is null || ipam.Records.Any(record => record is null) ||
            ipam.Subnets is null || ipam.Subnets.Any(subnet => subnet is null))
            throw new BackupArchiveException("The backup contains an invalid application data structure.");

        if (!users.Users.Any(user => user.IsEnabled && user.Roles.Contains(SystemRoles.Administrator, StringComparer.OrdinalIgnoreCase)))
            throw new BackupArchiveException("The backup must contain at least one active administrator.");
        if (settings.PollingIntervalSeconds is < 10 or > 3600 || settings.MaximumConcurrentChecks is < 1 or > 64 ||
            settings.PingTimeoutMilliseconds is < 250 or > 10000)
            throw new BackupArchiveException("The backup contains invalid application monitoring settings.");

        var deviceIds = new HashSet<Guid>();
        var deviceAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var locationIds = new HashSet<Guid>();
        foreach (var location in structure.Locations)
        {
            if (location.Id == Guid.Empty || !locationIds.Add(location.Id) ||
                location.ParentLocationId is { } parentId && !structure.Locations.Any(parent => parent.Id == parentId))
                throw new BackupArchiveException("The backup contains invalid location IDs or parent references.");
        }
        var groupIds = new HashSet<Guid>();
        foreach (var group in structure.Groups)
        {
            if (group.Id == Guid.Empty || !groupIds.Add(group.Id) ||
                group.ParentGroupId is { } parentId && !structure.Groups.Any(parent => parent.Id == parentId))
                throw new BackupArchiveException("The backup contains invalid group IDs or parent references.");
        }
        var subnetIds = new HashSet<Guid>();
        foreach (var subnet in ipam.Subnets)
        {
            if (subnet.Id == Guid.Empty || !subnetIds.Add(subnet.Id))
                throw new BackupArchiveException("The backup contains duplicate or empty subnet IDs.");
        }
        foreach (var device in devices.Devices)
        {
            if (device.Id == Guid.Empty || !deviceIds.Add(device.Id) || !TryNormalizeIp(device.IpAddress, out var normalizedDeviceAddress) ||
                !deviceAddresses.Add(normalizedDeviceAddress) ||
                device.LocationId is { } location && !locationIds.Contains(location) ||
                device.BuildingId is { } building && !locationIds.Contains(building) ||
                device.GroupId is { } group && !groupIds.Contains(group))
                throw new BackupArchiveException("The backup contains duplicate device IDs or invalid device assignments.");
        }

        var mapIds = new HashSet<Guid>();
        foreach (var map in maps.Maps)
        {
            if (map.Id == Guid.Empty || !mapIds.Add(map.Id) ||
                map.LocationId is { } location && !locationIds.Contains(location) ||
                map.BuildingId is { } building && !locationIds.Contains(building) ||
                map.Nodes.Any(node => !deviceIds.Contains(node.DeviceId)) ||
                map.Connections.Any(connection => !deviceIds.Contains(connection.FromDeviceId) || !deviceIds.Contains(connection.ToDeviceId)))
                throw new BackupArchiveException("The backup contains invalid network map references.");
        }

        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ipRecordIds = new HashSet<Guid>();
        foreach (var record in ipam.Records)
        {
            if (record.Id == Guid.Empty || !ipRecordIds.Add(record.Id) ||
                !TryNormalizeIp(record.IpAddress, out var normalizedRecordAddress) || !addresses.Add(normalizedRecordAddress) ||
                record.DeviceId is { } deviceId && !deviceIds.Contains(deviceId) ||
                record.SubnetId is { } subnetId && !subnetIds.Contains(subnetId) ||
                record.LocationId is { } locationId && !locationIds.Contains(locationId) ||
                record.BuildingId is { } buildingId && !locationIds.Contains(buildingId) ||
                record.GroupId is { } groupId && !groupIds.Contains(groupId))
                throw new BackupArchiveException("The backup contains duplicate IP records or invalid IPAM references.");
        }

        foreach (var subnet in ipam.Subnets)
        {
            var cidr = subnet.Cidr.Split('/', StringSplitOptions.TrimEntries);
            if (subnet.Id == Guid.Empty || cidr.Length != 2 ||
                !IPAddress.TryParse(cidr[0], out var subnetAddress) ||
                !int.TryParse(cidr[1], out var prefixLength) ||
                prefixLength < 0 || prefixLength > (subnetAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128) ||
                !IPAddress.TryParse(subnet.NetworkAddress, out var networkAddress) ||
                subnetAddress.AddressFamily != networkAddress.AddressFamily || subnet.PrefixLength != prefixLength ||
                subnet.LocationId is { } locationId && !locationIds.Contains(locationId) ||
                subnet.BuildingId is { } buildingId && !locationIds.Contains(buildingId))
                throw new BackupArchiveException("The backup contains invalid subnet data or references.");
        }

        static bool TryNormalizeIp(string? value, out string normalized)
        {
            if (IPAddress.TryParse(value, out var address))
            {
                normalized = address.ToString();
                return true;
            }
            normalized = string.Empty;
            return false;
        }
    }

    private static void ValidateManifestHeader(BackupManifest manifest)
    {
        if (!string.Equals(manifest.Format, "UniversalITMonitorBackup", StringComparison.Ordinal) || manifest.FormatVersion != 1)
            throw new BackupArchiveException("The backup format is not supported.");
        if (!Version.TryParse(manifest.ApplicationVersion, out var backupVersion) ||
            !Version.TryParse(GetApplicationVersion(), out var currentVersion) ||
            backupVersion.Major > currentVersion.Major)
            throw new BackupArchiveException("The backup was created by an incompatible application version.");
        if (manifest.CreatedUtc == default || manifest.CreatedUtc > DateTimeOffset.UtcNow.AddMinutes(5) || manifest.KeyProtection is null || manifest.Files is null)
            throw new BackupArchiveException("The backup manifest has invalid metadata.");
    }

    private static async Task<T> ReadJsonAsync<T>(string root, string relativePath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, ToPlatformPath(relativePath));
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new JsonException("A required backup JSON document is empty.");
    }

    private static async Task ExtractEntriesAsync(
        ZipArchive archive,
        string destinationRoot,
        IReadOnlyDictionary<string, BackupManifestFile> manifest,
        HashSet<string>? selectedPaths,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var entry in archive.Entries)
        {
            if (string.Equals(entry.FullName, ManifestPath, StringComparison.OrdinalIgnoreCase) ||
                selectedPaths is not null && !selectedPaths.Contains(entry.FullName)) continue;
            if (!manifest.TryGetValue(entry.FullName, out var manifestFile))
                throw new BackupArchiveException("The archive contents changed after validation.");

            var destination = GetSafeDestinationPath(destinationRoot, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var length = await CopyAndHashAsync(input, output, hash, cancellationToken);
            if (length != manifestFile.Length || !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(manifestFile.Sha256), hash.GetHashAndReset()))
                throw new BackupArchiveException("A backup file changed during staging and failed its integrity check.");
            await output.FlushAsync(cancellationToken);
        }
    }

    private static async Task<long> CopyAndHashAsync(Stream input, Stream output, IncrementalHash hash, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            total += count;
            if (total > MaximumEntrySize) throw new BackupArchiveException("A backup entry exceeds the supported size limit.");
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        return total;
    }

    private static IEnumerable<string> EnumeratePersistentFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var childDirectory in Directory.EnumerateDirectories(directory))
            {
                if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0)
                    throw new BackupArchiveException("The data directory contains a linked directory and cannot be backed up safely.");
                pending.Push(childDirectory);
            }

            foreach (var path in Directory.EnumerateFiles(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new BackupArchiveException("The data directory contains a linked file and cannot be backed up safely.");
                if (IsTransient(path)) continue;
                yield return path;
            }
        }
    }

    private static bool IsTransient(string path) =>
        path.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(path).Contains(".tmp-", StringComparison.OrdinalIgnoreCase);

    private static void ValidateArchivePath(ZipArchiveEntry entry)
    {
        ValidateManifestPath(entry.FullName);
        if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
            throw new BackupArchiveException("The backup archive contains a symbolic link entry.");
        if (entry.Length > MaximumEntrySize)
            throw new BackupArchiveException("A backup entry exceeds the supported size limit.");
    }

    private static void ValidateManifestPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 ||
            path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith('\\') ||
            path.Contains('\\') || path.Contains(':') || path.Contains('\0') || Path.IsPathRooted(path))
            throw new BackupArchiveException("The backup contains an unsafe file path.");

        var segments = path.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." ||
                                     segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new BackupArchiveException("The backup contains an unsafe file path.");
    }

    private static string GetSafeDestinationPath(string root, string relativePath)
    {
        var destination = Path.GetFullPath(Path.Combine(root, ToPlatformPath(relativePath)));
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new BackupArchiveException("The backup path escapes the staging directory.");
        return destination;
    }

    private static string ToArchivePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static string ToPlatformPath(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private static string GetApplicationVersion() =>
        typeof(BackupArchiveCodec).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";

    private static IReadOnlyList<BackupModule> GetAvailableModules(IEnumerable<string> paths) =>
        Enum.GetValues<BackupModule>().Where(module => paths.Any(path => BackupModulePaths.Owns(module, path))).ToArray();

    private static string? NormalizeThumbprint(string? thumbprint) =>
        string.IsNullOrWhiteSpace(thumbprint) ? null : new string(thumbprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record ValidatedBackupData(UserDocument Users, DeviceDocument Devices, InventoryStructureDocument Structure,
        NetworkMapDocument Maps, IpamDocument Ipam);
}

internal sealed record ValidatedBackup(BackupManifest Manifest, Dictionary<string, BackupManifestFile> Files, BackupPreview Preview);

public sealed class BackupArchiveException(string message) : Exception(message);

internal static class BackupModulePaths
{
    private static readonly IReadOnlyDictionary<BackupModule, string[]> ModulePaths = new Dictionary<BackupModule, string[]>
    {
        [BackupModule.DeviceInventory] = ["devices.json"],
        [BackupModule.LocationsAndGroups] = ["inventory-structure.json"],
        [BackupModule.NetworkMaps] = ["network-maps.json"],
        [BackupModule.Ipam] = ["ipam.json"],
        [BackupModule.Users] = ["users.json", "roles.json"],
        [BackupModule.ApplicationSettings] = ["settings.json", "branding.json", "themes.json", "inventory-reference-report.json"],
        [BackupModule.Notifications] = ["notification-settings.json", "notification-history.json"],
        [BackupModule.MonitoringHistory] = ["monitoring-history/", "monitoring-samples/"],
        [BackupModule.SnmpMetrics] = ["snmp-metrics/"]
    };

    public static bool Owns(BackupModule module, string path) =>
        ModulePaths.TryGetValue(module, out var prefixes) && prefixes.Any(prefix =>
            prefix.EndsWith("/", StringComparison.Ordinal)
                ? path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                : string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase));

    public static bool Owns(BackupModule module, string path, bool includePrefixDirectory) =>
        Owns(module, path) || includePrefixDirectory && ModulePaths.TryGetValue(module, out var prefixes) &&
        prefixes.Any(prefix => prefix.EndsWith("/", StringComparison.Ordinal) && string.Equals(path + "/", prefix, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> PathsFor(BackupModule module) => ModulePaths[module];
}
