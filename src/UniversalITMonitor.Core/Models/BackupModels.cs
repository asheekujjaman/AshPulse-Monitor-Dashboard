namespace UniversalITMonitor.Core.Models;

public sealed class BackupSettingsDocument
{
    public BackupSettings Settings { get; set; } = new();
}

public sealed class BackupSettings
{
    public bool AutomaticEnabled { get; set; }

    public int IntervalHours { get; set; } = 24;

    public int RetentionCount { get; set; } = 14;

    public string BackupDirectory { get; set; } = string.Empty;

    public DateTimeOffset? NextAutomaticUtc { get; set; }
}

public sealed class BackupHistoryDocument
{
    public List<BackupHistoryRecord> Records { get; set; } = [];
}

public sealed class BackupHistoryRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow;

    public BackupOperationKind Operation { get; set; }

    public bool Succeeded { get; set; }

    public string? FileName { get; set; }

    public long? SizeBytes { get; set; }

    public string? Details { get; set; }
}

public enum BackupOperationKind
{
    Manual,
    Automatic,
    Safety,
    Restore
}

public sealed class BackupManifest
{
    public string Format { get; set; } = "UniversalITMonitorBackup";

    public int FormatVersion { get; set; } = 1;

    public string ApplicationVersion { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public BackupKeyProtection KeyProtection { get; set; } = new();

    public List<BackupManifestFile> Files { get; set; } = [];
}

public sealed class BackupKeyProtection
{
    public string Mode { get; set; } = "Default";

    public string? CertificateThumbprint { get; set; }
}

public sealed class BackupManifestFile
{
    public string Path { get; set; } = string.Empty;

    public long Length { get; set; }

    public string Sha256 { get; set; } = string.Empty;
}

public enum BackupModule
{
    DeviceInventory,
    LocationsAndGroups,
    NetworkMaps,
    Ipam,
    Users,
    ApplicationSettings,
    Notifications,
    MonitoringHistory,
    SnmpMetrics
}

public sealed record BackupFileSummary(string Path, long Length, string Sha256);

public sealed record BackupPreview(
    string FileName,
    string ApplicationVersion,
    DateTimeOffset CreatedUtc,
    BackupKeyProtection KeyProtection,
    long TotalLength,
    IReadOnlyList<BackupFileSummary> Files,
    IReadOnlyList<BackupModule> AvailableModules);

public sealed record BackupFileInfo(string FileName, DateTimeOffset? CreatedUtc, long SizeBytes, bool IsSafetyBackup);

public sealed record BackupOperationResult(
    bool Succeeded,
    string? FileName = null,
    long? SizeBytes = null,
    string? Error = null,
    bool RequiresRestart = false);

public sealed record BackupRestoreRequest(
    string FileName,
    bool FullRestore,
    IReadOnlyCollection<BackupModule> Modules,
    bool ConfirmReplace);
