using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Core.Persistence;

public interface INetworkMapRepository
{
    Task<IReadOnlyList<NetworkMap>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<NetworkMap?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MapWriteResult> SaveAsync(NetworkMap map, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> RemoveDeviceReferencesAsync(Guid deviceId, CancellationToken cancellationToken = default);
}

public enum MapWriteResult
{
    Success,
    NotFound,
    DuplicateName,
    Invalid
}
