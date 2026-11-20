using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Core.Persistence;

public interface IIpamRepository
{
    Task<IReadOnlyList<IpamRecord>> GetRecordsAsync(CancellationToken cancellationToken = default);

    Task<IpamRecord?> GetRecordAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IpamWriteResult> SaveRecordAsync(IpamRecord record, CancellationToken cancellationToken = default);

    Task<IpamWriteResult> ChangeAllocationAsync(IpamRecord record, IpamHistoryEvent history, CancellationToken cancellationToken = default);

    Task<bool> DeleteRecordAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> DetachDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);

    Task<IpamImportStorageResult> AddRecordsIfUniqueAsync(IReadOnlyList<IpamRecord> records, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IpamSubnet>> GetSubnetsAsync(CancellationToken cancellationToken = default);

    Task<IpamSubnet?> GetSubnetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IpamWriteResult> SaveSubnetAsync(IpamSubnet subnet, CancellationToken cancellationToken = default);

    Task<IpamWriteResult> DeleteSubnetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IpamHistoryEvent>> GetHistoryAsync(CancellationToken cancellationToken = default);
}

public enum IpamWriteResult
{
    Success,
    NotFound,
    DuplicateIpAddress,
    DuplicateSubnet,
    Invalid,
    InUse,
    AllocationConflict
}

public sealed record IpamImportStorageResult(int ImportedCount, IReadOnlyList<string> DuplicateIpAddresses);
