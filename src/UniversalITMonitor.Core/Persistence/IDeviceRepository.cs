using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Core.Persistence;

public interface IDeviceRepository
{
    Task<IReadOnlyList<NetworkDevice>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<NetworkDevice?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DeviceWriteResult> CreateAsync(NetworkDevice device, CancellationToken cancellationToken = default);

    Task<DeviceWriteResult> UpdateAsync(NetworkDevice device, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DeviceWriteResult> SetMonitoringEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default);

    Task<DeviceCheckBatchResult> RecordChecksAsync(
        IReadOnlyCollection<DeviceCheckResult> results,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonitoringHistoryEntry>> GetPendingTransitionsAsync(CancellationToken cancellationToken = default);

    Task<bool> CompleteTransitionAsync(Guid transitionId, CancellationToken cancellationToken = default);

    Task<DeviceImportResult> ImportAsync(IReadOnlyCollection<NetworkDevice> devices, CancellationToken cancellationToken = default);

    Task UpdateAssignmentsAsync(IReadOnlyCollection<DeviceAssignmentUpdate> assignments, CancellationToken cancellationToken = default);
}

public enum DeviceWriteResult
{
    Success,
    NotFound,
    Ignored,
    DuplicateIpAddress,
    ValidationFailed
}

public sealed record DeviceImportResult(int Imported, IReadOnlyList<string> Errors);
