namespace UniversalITMonitor.Web.ViewModels;

public sealed record HierarchyChoice(Guid Id, string Name, IReadOnlyList<Guid>? ParentIds = null);
