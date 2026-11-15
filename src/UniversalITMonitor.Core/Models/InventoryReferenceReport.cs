namespace UniversalITMonitor.Core.Models;

public sealed class InventoryReferenceReportDocument
{
    public DateTimeOffset LastRunUtc { get; set; }

    public int RepairedCount { get; set; }

    public List<InventoryReferenceIssue> Issues { get; set; } = [];
}

public sealed class InventoryReferenceIssue
{
    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string Field { get; set; } = string.Empty;

    public string InvalidReference { get; set; } = string.Empty;

    public bool Repaired { get; set; }

    public string Detail { get; set; } = string.Empty;
}
