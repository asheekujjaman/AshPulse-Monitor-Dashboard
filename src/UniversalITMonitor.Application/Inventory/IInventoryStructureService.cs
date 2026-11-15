using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Inventory;

public interface IInventoryStructureService
{
    Task<InventoryStructureDocument> GetStructureAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocationSummary>> GetLocationSummariesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupSummary>> GetGroupSummariesAsync(CancellationToken cancellationToken = default);

    Task<string?> ValidateDeviceAssignmentAsync(Guid? locationId, Guid? buildingId, Guid? groupId, CancellationToken cancellationToken = default);

    Task<StructureWriteResult> CreateLocationAsync(ManagedLocation location, CancellationToken cancellationToken = default);

    Task<StructureWriteResult> UpdateLocationAsync(ManagedLocation location, CancellationToken cancellationToken = default);

    Task<HierarchyMutationResult> DeleteLocationAsync(Guid id, Guid? reassignToLocationId, bool confirmUnassignDevices, CancellationToken cancellationToken = default);

    Task<StructureWriteResult> CreateGroupAsync(ManagedGroup group, CancellationToken cancellationToken = default);

    Task<StructureWriteResult> UpdateGroupAsync(ManagedGroup group, CancellationToken cancellationToken = default);

    Task<HierarchyMutationResult> DeleteGroupAsync(Guid id, Guid? reassignToGroupId, bool confirmUnassignDevices, CancellationToken cancellationToken = default);
}

public sealed record HierarchyMutationResult(StructureWriteResult Result, int AffectedDeviceCount = 0, string? Error = null);

public sealed record LocationSummary(
    ManagedLocation Location,
    string Path,
    int DeviceCount,
    int UpCount,
    int DownCount,
    int UnknownCount,
    IReadOnlyList<Guid> DeviceIds);

public sealed record GroupSummary(
    ManagedGroup Group,
    string Path,
    int DeviceCount,
    int UpCount,
    int DownCount,
    int UnknownCount,
    IReadOnlyList<Guid> DeviceIds);
