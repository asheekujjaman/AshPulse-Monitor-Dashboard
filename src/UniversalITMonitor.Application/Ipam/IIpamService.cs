using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Application.Ipam;

public interface IIpamService
{
    Task<IReadOnlyList<IpamRecord>> GetRecordsAsync(CancellationToken cancellationToken = default);
    Task<IpamRecord?> GetRecordAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IpamMutationResult> SaveRecordAsync(IpamRecord record, CancellationToken cancellationToken = default);
    Task<IpamMutationResult> DeleteRecordAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IpamSubnet>> GetSubnetsAsync(CancellationToken cancellationToken = default);
    Task<IpamSubnet?> GetSubnetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IpamMutationResult> SaveSubnetAsync(IpamSubnet subnet, CancellationToken cancellationToken = default);
    Task<IpamMutationResult> DeleteSubnetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IpamCsvImportPreview> PreviewCsvImportAsync(string csv, IReadOnlyDictionary<string, string?> mapping, CancellationToken cancellationToken = default);
    Task<IpamCsvImportReport> ImportCsvAsync(string csv, IReadOnlyDictionary<string, string?> mapping, CancellationToken cancellationToken = default);
}

public sealed record IpamMutationResult(bool Success, Guid? Id = null, string? Error = null, IReadOnlyList<string>? ConflictingAddresses = null);

public sealed record IpamCsvPreviewRow(int RowNumber, IpamRecord? Record, IReadOnlyList<string> Errors, bool Duplicate);

public sealed record IpamCsvImportPreview(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IpamCsvPreviewRow> Rows,
    string? Error,
    int ValidCount,
    int InvalidCount,
    int DuplicateCount);

public sealed record IpamCsvImportReport(int ImportedCount, int InvalidCount, int DuplicateCount, IReadOnlyList<string> Errors, IReadOnlyList<string> DuplicateIpAddresses);
