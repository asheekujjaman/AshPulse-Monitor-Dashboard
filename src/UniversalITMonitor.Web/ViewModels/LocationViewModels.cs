using System.ComponentModel.DataAnnotations;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class LocationsIndexViewModel
{
    public IReadOnlyList<LocationSummary> Locations { get; init; } = [];
}

public sealed class LocationDetailsViewModel
{
    public required LocationSummary Summary { get; init; }
    public IReadOnlyList<DeviceInfo> Devices { get; init; } = [];
}

public sealed class LocationEditViewModel
{
    public Guid? Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    public LocationKind Kind { get; set; } = LocationKind.Site;

    public Guid? ParentLocationId { get; set; }

    public IReadOnlyList<HierarchyChoice> ParentChoices { get; set; } = [];

    public bool IsEditing => Id.HasValue;
}

public sealed class LocationDeleteViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DeviceCount { get; set; }
    public Guid? ReassignToLocationId { get; set; }
    public bool ConfirmUnassignDevices { get; set; }
    public string? Error { get; set; }
    public IReadOnlyList<HierarchyChoice> Choices { get; set; } = [];
}

public sealed class GroupsIndexViewModel
{
    public IReadOnlyList<GroupSummary> Groups { get; init; } = [];
}

public sealed class GroupDetailsViewModel
{
    public required GroupSummary Summary { get; init; }
    public IReadOnlyList<DeviceInfo> Devices { get; init; } = [];
}

public sealed class GroupEditViewModel
{
    public Guid? Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    public Guid? ParentGroupId { get; set; }

    public IReadOnlyList<HierarchyChoice> ParentChoices { get; set; } = [];

    public bool IsEditing => Id.HasValue;
}

public sealed class GroupDeleteViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DeviceCount { get; set; }
    public Guid? ReassignToGroupId { get; set; }
    public bool ConfirmUnassignDevices { get; set; }
    public string? Error { get; set; }
    public IReadOnlyList<HierarchyChoice> Choices { get; set; } = [];
}
