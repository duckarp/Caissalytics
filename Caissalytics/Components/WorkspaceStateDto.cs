namespace Caissalytics.Components;

public class WorkspaceStateDto
{
    public Guid ActiveTabId { get; set; }
    public List<WorkspaceTabDto> Tabs { get; set; } = new();
}

public class WorkspaceTabDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "dashboard"; // "dashboard", "analysis", "database"
    public string? Title { get; set; }
    public string? Pgn { get; set; }
    public List<int>? CurrentNodePath { get; set; }
    public string? Orientation { get; set; }
    public bool IsAnalyzing { get; set; }
    public int MultiPv { get; set; } = 3;
    public string? TargetDatabase { get; set; }
    public long? DatabaseGameId { get; set; }
}
