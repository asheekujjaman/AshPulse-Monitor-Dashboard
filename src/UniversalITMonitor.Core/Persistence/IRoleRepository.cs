using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Core.Persistence;

public interface IRoleRepository
{
    Task<IReadOnlyList<RoleDefinition>> GetAllAsync(CancellationToken cancellationToken = default);
}
