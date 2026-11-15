using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Application.Backups;

public interface IBackupService
{
    Task<BackupSettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<BackupOperationResult> SaveSettingsAsync(BackupSettings settings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupHistoryRecord>> GetHistoryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupFileInfo>> GetBackupFilesAsync(CancellationToken cancellationToken = default);

    Task<BackupOperationResult> CreateManualBackupAsync(CancellationToken cancellationToken = default);

    Task<BackupOperationResult> CreateDueAutomaticBackupAsync(CancellationToken cancellationToken = default);

    Task<BackupPreview> PreviewAsync(string fileName, CancellationToken cancellationToken = default);

    Task<Stream> OpenBackupAsync(string fileName, CancellationToken cancellationToken = default);

    Task<BackupOperationResult> RestoreAsync(BackupRestoreRequest request, CancellationToken cancellationToken = default);
}

public sealed record BackupKeyProtectionOptions(
    string Mode,
    string? CertificateThumbprint = null,
    System.Security.Cryptography.X509Certificates.X509Certificate2? Certificate = null,
    bool UseWindowsDpapi = false);
