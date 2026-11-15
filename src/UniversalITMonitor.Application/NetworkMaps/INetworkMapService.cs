using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.NetworkMaps;

public interface INetworkMapService
{
    Task<IReadOnlyList<NetworkMap>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<NetworkMap?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MapMutationResult> SaveAsync(NetworkMap map, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MapImportPreview> PreviewImportAsync(string json, CancellationToken cancellationToken = default);

    Task<MapMutationResult> ImportAsync(NetworkMap map, string? newName = null, CancellationToken cancellationToken = default);
}

public sealed record MapMutationResult(MapWriteResult Result, string? Error = null, IReadOnlyList<Guid>? MissingDeviceIds = null, Guid? MapId = null);

public sealed record MapImportPreview(NetworkMap? Map, IReadOnlyList<Guid> MissingDeviceIds, string? Error);
