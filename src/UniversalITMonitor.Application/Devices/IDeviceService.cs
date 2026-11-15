using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Devices;

public interface IDeviceService
{
    Task<IReadOnlyList<DeviceInfo>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<DeviceInfo?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DeviceCommandResult> CreateAsync(DeviceEditInput input, CancellationToken cancellationToken = default);

    Task<DeviceCommandResult> UpdateAsync(DeviceEditInput input, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DeviceWriteResult> SetMonitoringEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NetworkDevice>> GetForMonitoringAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonitoringHistoryEntry>> GetHistoryAsync(Guid id, int limit = 100, CancellationToken cancellationToken = default);

    Task<DeviceImportResult> ImportAsync(IReadOnlyCollection<NetworkDevice> devices, CancellationToken cancellationToken = default);
}

public sealed record DeviceCommandResult(DeviceWriteResult Result, string? Error = null)
{
    public bool Succeeded => Result == DeviceWriteResult.Success;
}
