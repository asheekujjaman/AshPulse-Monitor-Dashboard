namespace UniversalITMonitor.Core.Models;

public enum LocationKind
{
    Site,
    Building,
    Area
}

public sealed class ManagedLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public LocationKind Kind { get; set; } = LocationKind.Site;

    public Guid? ParentLocationId { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ManagedGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public Guid? ParentGroupId { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class InventoryStructureDocument
{
    public List<ManagedLocation> Locations { get; set; } = [];

    public List<ManagedGroup> Groups { get; set; } = [];
}

public sealed record ResolvedDeviceAssignment(Guid? LocationId, Guid? BuildingId, Guid? GroupId);

public sealed record DeviceAssignmentUpdate(
    Guid DeviceId,
    Guid? LocationId,
    Guid? BuildingId,
    Guid? GroupId,
    string? Location,
    string? Building,
    string? Group);
