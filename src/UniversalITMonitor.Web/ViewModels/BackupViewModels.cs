using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class BackupIndexViewModel
{
    public BackupSettings Settings { get; init; } = new();

    public IReadOnlyList<BackupHistoryRecord> History { get; init; } = [];

    public IReadOnlyList<BackupFileInfo> Files { get; init; } = [];

    public string? Error { get; init; }
}

public sealed class BackupRestoreViewModel
{
    public BackupPreview? Preview { get; set; }

    public string FileName { get; init; } = string.Empty;

    public bool FullRestore { get; set; } = true;

    public List<BackupModule> Modules { get; set; } = [];

    public bool ConfirmReplace { get; set; }

    public string? Error { get; set; }
}
