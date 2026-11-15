using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Backups;

public sealed class BackupService : IBackupService
{
    private const string SettingsFile = "backup-settings.json";
    private const string HistoryFile = "backup-history.json";
    private const int HistoryLimit = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dataDirectory;
    private readonly string _webRoot;
    private readonly string? _contentRoot;
    private readonly bool _requireExternalDirectories;
    private readonly BackupKeyProtectionOptions _keyOptions;
    private readonly IJsonDocumentStore _documents;
    private readonly IApplicationMaintenanceLock _maintenanceLock;
    private readonly ILogger<BackupService> _logger;
    private readonly BackupArchiveCodec _codec;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public BackupService(
        string dataDirectory,
        string webRoot,
        BackupKeyProtectionOptions keyOptions,
        IJsonDocumentStore documents,
        IApplicationMaintenanceLock maintenanceLock,
        ILogger<BackupService> logger,
        string? contentRoot = null,
        bool requireExternalDirectories = false)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _webRoot = Path.GetFullPath(webRoot);
        _contentRoot = string.IsNullOrWhiteSpace(contentRoot) ? null : Path.GetFullPath(contentRoot);
        _requireExternalDirectories = requireExternalDirectories;
        _keyOptions = keyOptions;
        _documents = documents;
        _maintenanceLock = maintenanceLock;
        _logger = logger;
        _codec = new BackupArchiveCodec(keyOptions, logger);
    }

    public async Task<BackupSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var document = await _documents.ReadAsync(SettingsFile, DefaultSettingsDocument, cancellationToken);
        var settings = NormalizeSettings(document.Settings);
        settings.BackupDirectory = ValidateBackupDirectory(settings.BackupDirectory, createDirectory: false);
        return settings;
    }

    public async Task<BackupOperationResult> SaveSettingsAsync(BackupSettings settings, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            await using var maintenance = await _maintenanceLock.EnterWriteAsync(cancellationToken);
            var normalized = NormalizeSettings(settings);
            if (settings.IntervalHours is < 1 or > 168 || settings.RetentionCount is < 1 or > 365)
                throw new ArgumentException("Backup interval must be 1–168 hours and retention must be 1–365 archives.");
            ValidateSettings(normalized);
            normalized.BackupDirectory = ValidateBackupDirectory(normalized.BackupDirectory, createDirectory: true);
            normalized.NextAutomaticUtc = normalized.AutomaticEnabled
                ? DateTimeOffset.UtcNow.AddHours(normalized.IntervalHours)
                : null;
            await _documents.WriteAsync(SettingsFile, new BackupSettingsDocument { Settings = normalized }, cancellationToken);
            return new BackupOperationResult(true);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            LogFailure("Backup settings could not be saved", exception);
            return new BackupOperationResult(false, Error: SafeFailure(exception));
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<IReadOnlyList<BackupHistoryRecord>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        var document = await _documents.ReadAsync(HistoryFile, static () => new BackupHistoryDocument(), cancellationToken);
        return document.Records.OrderByDescending(record => record.OccurredUtc).ToArray();
    }

    public async Task<IReadOnlyList<BackupFileInfo>> GetBackupFilesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        var directory = ValidateBackupDirectory(settings.BackupDirectory, createDirectory: false);
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "uim-backup-*.zip", SearchOption.TopDirectoryOnly)
            .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new BackupFileInfo(info.Name, info.LastWriteTimeUtc, info.Length,
                    info.Name.StartsWith("uim-backup-safety-", StringComparison.OrdinalIgnoreCase));
            })
            .OrderByDescending(file => file.CreatedUtc)
            .ToArray();
    }

    public Task<BackupOperationResult> CreateManualBackupAsync(CancellationToken cancellationToken = default) =>
        CreateBackupAsync(BackupOperationKind.Manual, cancellationToken);

    public async Task<BackupOperationResult> CreateDueAutomaticBackupAsync(CancellationToken cancellationToken = default)
    {
        var observedSettings = await GetSettingsAsync(cancellationToken);
        if (!observedSettings.AutomaticEnabled || observedSettings.NextAutomaticUtc is null || observedSettings.NextAutomaticUtc > DateTimeOffset.UtcNow)
            return new BackupOperationResult(true);

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            await using var maintenance = await _maintenanceLock.EnterWriteAsync(cancellationToken);
            var settings = await GetSettingsAsync(cancellationToken);
            if (!settings.AutomaticEnabled || settings.NextAutomaticUtc is null || settings.NextAutomaticUtc > DateTimeOffset.UtcNow)
                return new BackupOperationResult(true);

            settings.NextAutomaticUtc = DateTimeOffset.UtcNow.AddHours(settings.IntervalHours);
            await _documents.WriteAsync(SettingsFile, new BackupSettingsDocument { Settings = settings }, cancellationToken);
            return await CreateBackupUnderLockAsync(BackupOperationKind.Automatic, settings, cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<BackupPreview> PreviewAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var path = await ResolveBackupPathAsync(fileName, cancellationToken);
        var validated = await _codec.ValidateAsync(path, fileName, cancellationToken);
        return validated.Preview;
    }

    public async Task<Stream> OpenBackupAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var path = await ResolveBackupPathAsync(fileName, cancellationToken);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public async Task<BackupOperationResult> RestoreAsync(BackupRestoreRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.ConfirmReplace)
            return new BackupOperationResult(false, Error: "Confirm replacement of existing data before restoring.");
        if (!request.FullRestore && (request.Modules.Count == 0 || request.Modules.Distinct().Count() != request.Modules.Count))
            return new BackupOperationResult(false, Error: "Select one or more modules to restore.");
        if (!request.FullRestore && request.Modules.Any(module => !Enum.IsDefined(module)))
            return new BackupOperationResult(false, Error: "The restore selection contains an unsupported module.");

        await _operationLock.WaitAsync(cancellationToken);
        string? stageDirectory = null;
        string? previousDirectory = null;
        var committed = false;
        BackupSettings? currentSettings = null;
        try
        {
            await using var maintenance = await _maintenanceLock.EnterWriteAsync(cancellationToken);
            currentSettings = await GetSettingsAsync(cancellationToken);
            var backupDirectory = ValidateBackupDirectory(currentSettings.BackupDirectory, createDirectory: true);
            var archivePath = ResolveBackupPath(fileName: request.FileName, backupDirectory);

            // Validate every archive entry and application document before creating or changing live data.
            var validated = await _codec.ValidateAsync(archivePath, request.FileName, cancellationToken);
            if (!request.FullRestore && request.Modules.Any(module => !validated.Preview.AvailableModules.Contains(module)))
                return new BackupOperationResult(false, Error: "The selected backup does not contain every requested module.");

            var parent = Path.GetDirectoryName(_dataDirectory)!;
            Directory.CreateDirectory(parent);
            stageDirectory = _dataDirectory + ".restore-stage-" + Guid.NewGuid().ToString("N");
            previousDirectory = _dataDirectory + ".restore-previous-" + Guid.NewGuid().ToString("N");
            if (request.FullRestore)
            {
                CreateRestoreStage(stageDirectory);
                await _codec.ExtractAllAsync(archivePath, stageDirectory, validated, cancellationToken);
            }
            else
            {
                CopyDirectory(_dataDirectory, stageDirectory);
                foreach (var module in request.Modules.Distinct()) RemoveModuleData(stageDirectory, module);
                await _codec.ExtractModulesAsync(archivePath, stageDirectory, validated, request.Modules, cancellationToken);
            }

            // Preserve destination-specific backup path and retain both sides' audit history.
            var restoredSettings = ReadSettingsFrom(stageDirectory);
            ValidateSettings(restoredSettings);
            restoredSettings.BackupDirectory = backupDirectory;
            if (restoredSettings.AutomaticEnabled)
                restoredSettings.NextAutomaticUtc ??= DateTimeOffset.UtcNow.AddHours(restoredSettings.IntervalHours);
            WriteJsonAtomically(stageDirectory, SettingsFile, new BackupSettingsDocument { Settings = restoredSettings });
            var combinedHistory = MergeHistories(ReadHistoryFrom(stageDirectory),
                await _documents.ReadAsync(HistoryFile, static () => new BackupHistoryDocument(), cancellationToken));
            WriteJsonAtomically(stageDirectory, HistoryFile, combinedHistory);

            var stagedKeyProtection = request.FullRestore
                ? validated.Manifest.KeyProtection
                : new BackupKeyProtection { Mode = _keyOptions.Mode, CertificateThumbprint = _keyOptions.CertificateThumbprint };
            await _codec.ValidateDataDirectoryAsync(stageDirectory, stagedKeyProtection, cancellationToken);

            var safety = await CreateBackupUnderLockAsync(BackupOperationKind.Safety, currentSettings, cancellationToken);
            if (!safety.Succeeded)
                return new BackupOperationResult(false, Error: "The safety backup failed; live data was not changed.");

            // Record safety backup in the staged history too, so it remains visible after a full restore.
            AppendHistory(combinedHistory, new BackupHistoryRecord
            {
                Operation = BackupOperationKind.Safety,
                Succeeded = true,
                FileName = safety.FileName,
                SizeBytes = safety.SizeBytes,
                Details = "Created before restore."
            });
            WriteJsonAtomically(stageDirectory, HistoryFile, combinedHistory);

            Directory.Move(_dataDirectory, previousDirectory);
            try
            {
                Directory.Move(stageDirectory, _dataDirectory);
                committed = true;
            }
            catch
            {
                Directory.Move(previousDirectory, _dataDirectory);
                previousDirectory = null;
                throw;
            }

            await TryWriteRestoreHistoryAsync(request.FileName, request.FullRestore, CancellationToken.None);
            return new BackupOperationResult(true, request.FileName, new FileInfo(archivePath).Length, RequiresRestart: request.FullRestore);
        }
        catch (Exception exception) when (exception is BackupArchiveException or IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or JsonException or CryptographicException)
        {
            LogFailure("Backup restore failed", exception);
            await TryWriteFailureHistoryAsync(request.FileName, exception, CancellationToken.None);
            return new BackupOperationResult(false, Error: SafeFailure(exception));
        }
        finally
        {
            if (!committed && stageDirectory is not null) TryDeleteDirectory(stageDirectory);
            if (committed && previousDirectory is not null) TryDeleteDirectory(previousDirectory);
            _operationLock.Release();
        }
    }

    private async Task<BackupOperationResult> CreateBackupAsync(BackupOperationKind kind, CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            await using var maintenance = await _maintenanceLock.EnterWriteAsync(cancellationToken);
            var settings = await GetSettingsAsync(cancellationToken);
            return await CreateBackupUnderLockAsync(kind, settings, cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<BackupOperationResult> CreateBackupUnderLockAsync(
        BackupOperationKind kind,
        BackupSettings settings,
        CancellationToken cancellationToken)
    {
        string? fileName = null;
        try
        {
            var directory = ValidateBackupDirectory(settings.BackupDirectory, createDirectory: true);
            var prefix = kind == BackupOperationKind.Safety ? "uim-backup-safety-" : "uim-backup-";
            fileName = prefix + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8] + ".zip";
            var path = Path.Combine(directory, fileName);
            await _codec.CreateAsync(_dataDirectory, path, cancellationToken);
            var size = new FileInfo(path).Length;
            await RecordHistoryAsync(new BackupHistoryRecord
            {
                Operation = kind,
                Succeeded = true,
                FileName = fileName,
                SizeBytes = size,
                Details = "Backup created and integrity verified."
            }, cancellationToken);
            if (kind is BackupOperationKind.Manual or BackupOperationKind.Automatic)
                ApplyRetention(directory, settings.RetentionCount);
            return new BackupOperationResult(true, fileName, size);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or BackupArchiveException or InvalidDataException or CryptographicException)
        {
            LogFailure("Backup creation failed", exception);
            await TryRecordHistoryAsync(new BackupHistoryRecord
            {
                Operation = kind,
                Succeeded = false,
                FileName = fileName,
                Details = SafeFailure(exception)
            }, cancellationToken);
            return new BackupOperationResult(false, Error: SafeFailure(exception));
        }
    }

    private async Task<string> ResolveBackupPathAsync(string fileName, CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        var directory = ValidateBackupDirectory(settings.BackupDirectory, createDirectory: false);
        return ResolveBackupPath(fileName, directory);
    }

    private static string ResolveBackupPath(string fileName, string directory)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName ||
            !fileName.StartsWith("uim-backup-", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Select a backup file from the configured backup directory.");
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new FileNotFoundException("The selected backup file does not exist.");
        return path;
    }

    private string ValidateBackupDirectory(string? configuredDirectory, bool createDirectory)
    {
        var value = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(Path.GetDirectoryName(_dataDirectory)!, "Backups")
            : configuredDirectory.Trim();
        if (!Path.IsPathRooted(value)) throw new ArgumentException("The backup directory must be an absolute path.");
        var full = Path.GetFullPath(value);
        var defaultBackupDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_dataDirectory)!, "Backups"));
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var isDefaultAppDataBackupDirectory = string.Equals(full, defaultBackupDirectory, pathComparison);
        if (IsSameOrChild(_dataDirectory, full) || IsSameOrChild(full, _dataDirectory) ||
            IsSameOrChild(_webRoot, full) || IsSameOrChild(full, _webRoot) ||
            _requireExternalDirectories && _contentRoot is not null && IsSameOrChild(_contentRoot, full) && !isDefaultAppDataBackupDirectory)
            throw new ArgumentException("The backup directory must be separate from the data directory and web root. In Production, only the default Backups folder beside App_Data may be inside the published application directory.");

        RejectReparsePath(full);
        if (createDirectory) Directory.CreateDirectory(full);
        return full;
    }

    private static void RejectReparsePath(string path)
    {
        var current = Path.GetPathRoot(path)!;
        foreach (var segment in path[Path.GetPathRoot(path)!.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0) continue;
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("The backup directory cannot include linked directories.");
        }
    }

    private static bool IsSameOrChild(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static BackupSettingsDocument DefaultSettingsDocument() => new()
    {
        Settings = new BackupSettings { BackupDirectory = string.Empty }
    };

    private BackupSettings NormalizeSettings(BackupSettings? settings) => new()
    {
        AutomaticEnabled = settings?.AutomaticEnabled ?? false,
        IntervalHours = settings?.IntervalHours is >= 1 and <= 168 ? settings.IntervalHours : 24,
        RetentionCount = settings?.RetentionCount is >= 1 and <= 365 ? settings.RetentionCount : 14,
        BackupDirectory = string.IsNullOrWhiteSpace(settings?.BackupDirectory)
            ? Path.Combine(Path.GetDirectoryName(_dataDirectory)!, "Backups")
            : settings.BackupDirectory,
        NextAutomaticUtc = settings?.NextAutomaticUtc
    };

    private static void ValidateSettings(BackupSettings settings)
    {
        if (settings.IntervalHours is < 1 or > 168) throw new ArgumentException("Backup interval must be from 1 to 168 hours.");
        if (settings.RetentionCount is < 1 or > 365) throw new ArgumentException("Retention must be from 1 to 365 backups.");
    }

    private async Task RecordHistoryAsync(BackupHistoryRecord record, CancellationToken cancellationToken)
    {
        await _documents.UpdateAsync<BackupHistoryDocument, bool>(HistoryFile,
            static () => new BackupHistoryDocument(), document =>
            {
                AppendHistory(document, record);
                return (document, true);
            }, cancellationToken);
    }

    private async Task TryRecordHistoryAsync(BackupHistoryRecord record, CancellationToken cancellationToken)
    {
        try { await RecordHistoryAsync(record, cancellationToken); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogFailure("Backup history could not be updated", exception);
        }
    }

    private async Task TryWriteRestoreHistoryAsync(string fileName, bool fullRestore, CancellationToken cancellationToken)
    {
        try
        {
            await RecordHistoryAsync(new BackupHistoryRecord
            {
                Operation = BackupOperationKind.Restore,
                Succeeded = true,
                FileName = fileName,
                Details = fullRestore ? "Full restore completed; application restart required." : "Selected modules restored."
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogFailure("Restore history could not be updated", exception);
        }
    }

    private async Task TryWriteFailureHistoryAsync(string fileName, Exception failure, CancellationToken cancellationToken)
    {
        try
        {
            await RecordHistoryAsync(new BackupHistoryRecord
            {
                Operation = BackupOperationKind.Restore,
                Succeeded = false,
                FileName = Path.GetFileName(fileName),
                Details = SafeFailure(failure)
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogFailure("Restore failure history could not be updated", exception);
        }
    }

    private static void AppendHistory(BackupHistoryDocument document, BackupHistoryRecord record)
    {
        document.Records.Add(record);
        document.Records = document.Records.OrderByDescending(item => item.OccurredUtc).Take(HistoryLimit).ToList();
    }

    private static BackupHistoryDocument MergeHistories(BackupHistoryDocument incoming, BackupHistoryDocument existing)
    {
        var records = incoming.Records.Concat(existing.Records)
            .GroupBy(record => record.Id)
            .Select(group => group.OrderByDescending(record => record.OccurredUtc).First())
            .OrderByDescending(record => record.OccurredUtc)
            .Take(HistoryLimit)
            .ToList();
        return new BackupHistoryDocument { Records = records };
    }

    private static BackupSettings ReadSettingsFrom(string directory)
    {
        var path = Path.Combine(directory, SettingsFile);
        if (!File.Exists(path)) return new BackupSettings();
        var document = JsonSerializer.Deserialize<BackupSettingsDocument>(File.ReadAllText(path), JsonOptions);
        return document?.Settings ?? throw new InvalidDataException("The restored backup settings are invalid.");
    }

    private static BackupHistoryDocument ReadHistoryFrom(string directory)
    {
        var path = Path.Combine(directory, HistoryFile);
        if (!File.Exists(path)) return new BackupHistoryDocument();
        return JsonSerializer.Deserialize<BackupHistoryDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The restored backup history is invalid.");
    }

    private static void WriteJsonAtomically<T>(string directory, string fileName, T document)
    {
        var path = Path.Combine(directory, fileName);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("The application data directory is missing.");
        Directory.CreateDirectory(destination);
        CopyDirectoryAccessRules(source, destination);
        var pending = new Stack<(string Source, string Relative)>();
        pending.Push((source, string.Empty));
        while (pending.Count > 0)
        {
            var (currentSource, relative) = pending.Pop();
            var currentDestination = relative.Length == 0 ? destination : Path.Combine(destination, relative);
            foreach (var directory in Directory.EnumerateDirectories(currentSource))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The application data directory contains a linked directory.");
                var childRelative = Path.Combine(relative, Path.GetFileName(directory));
                var copy = Path.Combine(destination, childRelative);
                Directory.CreateDirectory(copy);
                CopyDirectoryAccessRules(directory, copy);
                pending.Push((directory, childRelative));
            }

            foreach (var file in Directory.EnumerateFiles(currentSource))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The application data directory contains a linked file.");
                var name = Path.GetFileName(file);
                if (name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains(".tmp-", StringComparison.OrdinalIgnoreCase)) continue;
                var path = Path.Combine(currentDestination, name);
                File.Copy(file, path);
                CopyFileAccessRules(file, path);
            }
        }
    }

    private void CreateRestoreStage(string directory)
    {
        Directory.CreateDirectory(directory);
        CopyDirectoryAccessRules(_dataDirectory, directory);
    }

    private static void CopyDirectoryAccessRules(string source, string destination)
    {
        if (!OperatingSystem.IsWindows()) return;
        var security = new DirectoryInfo(source).GetAccessControl();
        new DirectoryInfo(destination).SetAccessControl(security);
    }

    private static void CopyFileAccessRules(string source, string destination)
    {
        if (!OperatingSystem.IsWindows()) return;
        var security = new FileInfo(source).GetAccessControl();
        new FileInfo(destination).SetAccessControl(security);
    }

    private static void RemoveModuleData(string stageDirectory, BackupModule module)
    {
        foreach (var item in BackupModulePaths.PathsFor(module))
        {
            if (item.EndsWith("/", StringComparison.Ordinal))
            {
                var directory = Path.Combine(stageDirectory, item.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            else
            {
                var path = Path.Combine(stageDirectory, item);
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            }
        }
    }

    private static void ApplyRetention(string directory, int retentionCount)
    {
        var files = Directory.EnumerateFiles(directory, "uim-backup-*.zip", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).StartsWith("uim-backup-safety-", StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToArray();
        foreach (var old in files.Skip(retentionCount))
        {
            if ((File.GetAttributes(old.FullName) & FileAttributes.ReparsePoint) == 0) old.Delete();
        }
    }

    private void LogFailure(string operation, Exception exception) =>
        _logger.LogWarning("{Operation} ({FailureType}).", operation, exception.GetType().Name);

    private static string SafeFailure(Exception exception) => exception switch
    {
        BackupArchiveException => exception.Message,
        ArgumentException => "The backup settings or selected file path is invalid.",
        UnauthorizedAccessException => "Access to the configured backup or data directory was denied.",
        FileNotFoundException or DirectoryNotFoundException => "The selected backup or required directory was not found.",
        InvalidDataException or JsonException => "Backup data is invalid or incompatible.",
        CryptographicException => "Backup credentials cannot be opened with the available Data Protection keys.",
        IOException => "An I/O failure prevented the backup operation.",
        _ => "The backup operation failed. Check server permissions and application logs."
    };

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
