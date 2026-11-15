using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Storage.Json;

public sealed class JsonRoleRepository(IJsonDocumentStore documents) : IRoleRepository
{
    public async Task<IReadOnlyList<RoleDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var roleDocument = await documents.ReadAsync("roles.json", static () => new RoleDocument(), cancellationToken);
        return roleDocument.Roles.ToArray();
    }
}
