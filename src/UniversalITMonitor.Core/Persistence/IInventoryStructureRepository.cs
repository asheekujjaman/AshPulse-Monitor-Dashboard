using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Core.Persistence;

public interface IInventoryStructureRepository
{
    Task<InventoryStructureDocument> GetAsync(CancellationToken cancellationToken = default);

    Task<StructureWriteResult> CreateLocationAsync(ManagedLocation location, CancellationToken cancellationToken = default);

    Task<StructureWriteResult> UpdateLocationAsync(ManagedLocation location, CancellationToken cancellationToken = default);

    Task<StructureWriteResult> DeleteLocationAsync(Guid id, CancellationToken cancellationToken = default);

    Task<StructureWriteResult> CreateGroupAsync(ManagedGroup group, CancellationToken cancellationToken = default);

    Task<StructureWriteResult> UpdateGroupAsync(ManagedGroup group, CancellationToken cancellationToken = default);

    Task<StructureWriteResult> DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ResolvedDeviceAssignment> ResolveOrCreateLegacyAssignmentAsync(
        string? location,
        string? building,
        string? group,
        CancellationToken cancellationToken = default);
}

public enum StructureWriteResult
{
    Success,
    NotFound,
    InvalidName,
    DuplicateName,
    InvalidParent,
    HasChildren,
    InUse
}
