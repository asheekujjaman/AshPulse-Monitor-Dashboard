using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class IpamAllocationFormModel
{
    public string IpAddress { get; init; } = string.Empty;
    public Guid SubnetId { get; init; }
    public IpAssignmentStatus CurrentStatus { get; init; }
    public string? AssignedTo { get; init; }
    public string? Note { get; init; }
}
