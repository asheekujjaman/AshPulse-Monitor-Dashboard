using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Storage.Json;

public sealed class JsonNetworkMapRepository(IJsonDocumentStore documents) : INetworkMapRepository
{
    private const string DocumentName = "network-maps.json";

    public async Task<IReadOnlyList<NetworkMap>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var document = await documents.ReadAsync(DocumentName, static () => new NetworkMapDocument(), cancellationToken);
        return document.Maps.OrderBy(map => map.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<NetworkMap?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await documents.ReadAsync(DocumentName, static () => new NetworkMapDocument(), cancellationToken);
        return document.Maps.FirstOrDefault(map => map.Id == id);
    }

    public Task<MapWriteResult> SaveAsync(NetworkMap map, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<NetworkMapDocument, MapWriteResult>(
            DocumentName,
            static () => new NetworkMapDocument(),
            document =>
            {
                if (document.Maps.Any(existing => existing.Id != map.Id &&
                    existing.LocationId == map.LocationId && existing.BuildingId == map.BuildingId &&
                    string.Equals(existing.Name, map.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    return (document, MapWriteResult.DuplicateName);
                }

                var index = document.Maps.FindIndex(existing => existing.Id == map.Id);
                if (index < 0)
                {
                    if (map.Id == Guid.Empty) map.Id = Guid.NewGuid();
                    map.CreatedUtc = DateTimeOffset.UtcNow;
                    map.UpdatedUtc = map.CreatedUtc;
                    document.Maps.Add(map);
                }
                else
                {
                    map.CreatedUtc = document.Maps[index].CreatedUtc;
                    map.UpdatedUtc = DateTimeOffset.UtcNow;
                    document.Maps[index] = map;
                }

                return (document, MapWriteResult.Success);
            },
            cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<NetworkMapDocument, bool>(
            DocumentName,
            static () => new NetworkMapDocument(),
            document => (document, document.Maps.RemoveAll(map => map.Id == id) > 0),
            cancellationToken);

    public Task<int> RemoveDeviceReferencesAsync(Guid deviceId, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<NetworkMapDocument, int>(
            DocumentName,
            static () => new NetworkMapDocument(),
            document =>
            {
                var removed = 0;
                foreach (var map in document.Maps)
                {
                    var removedNodes = map.Nodes.RemoveAll(node => node.DeviceId == deviceId);
                    var removedConnections = map.Connections.RemoveAll(connection =>
                        connection.FromDeviceId == deviceId || connection.ToDeviceId == deviceId);
                    if (removedNodes > 0 || removedConnections > 0)
                    {
                        removed += removedNodes + removedConnections;
                        map.UpdatedUtc = DateTimeOffset.UtcNow;
                    }
                }

                return (document, removed);
            },
            cancellationToken);
}
