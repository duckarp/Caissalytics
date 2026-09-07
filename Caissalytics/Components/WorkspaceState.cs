using Caissalytics.Core;
using Caissalytics.Engine;

namespace Caissalytics.Components;

public abstract class WorkspaceTab
{
    public Guid Id { get; } = Guid.NewGuid();
    public abstract string Title { get; }
    public abstract string Icon { get; }
    public bool CanClose { get; protected set; } = true;
}

public class DashboardTab : WorkspaceTab
{
    public override string Title => "Dashboard";
    public override string Icon => "🏠";

    public DashboardTab()
    {
        CanClose = false;
    }
}

public class AnalysisTab : WorkspaceTab
{
    private string _customTitle;
    public override string Title => _customTitle;
    public override string Icon => "♟️";

    public GameTree Tree { get; set; }
    public string Orientation { get; set; } = "white";
    public bool IsAnalyzing { get; set; } = false;
    public int MultiPv { get; set; } = 3;
    public List<EngineEvaluationLine> CurrentEvalLines { get; set; } = new();
    public string? TargetDatabase { get; set; }
    public long? DatabaseGameId { get; set; }

    public AnalysisTab(string? title = null, string? pgn = null, string? startFen = null, string? targetDatabase = null, long? databaseGameId = null)
    {
        _customTitle = title ?? "Analysis Board";
        TargetDatabase = targetDatabase;
        DatabaseGameId = databaseGameId;
        Tree = !string.IsNullOrWhiteSpace(pgn)
            ? PgnHandler.ImportPgn(pgn)
            : new GameTree(startFen);
    }

    public void SetTitle(string title)
    {
        _customTitle = title;
    }
}

public class DatabaseBrowserTab : WorkspaceTab
{
    public override string Title => "Database";
    public override string Icon => "🗄️";
}

public class WorkspaceState
{
    public List<WorkspaceTab> Tabs { get; } = new();
    public WorkspaceTab ActiveTab { get; private set; }
    public event Action? OnChange;

    public WorkspaceState()
    {
        var dashboard = new DashboardTab();
        Tabs.Add(dashboard);
        ActiveTab = dashboard;
    }

    public AnalysisTab CreateAnalysisTab(string? title = null, string? pgn = null, string? startFen = null, string? targetDatabase = null, long? databaseGameId = null)
    {
        int analysisCount = Tabs.OfType<AnalysisTab>().Count() + 1;
        string finalTitle = title ?? $"Analysis {analysisCount}";

        var tab = new AnalysisTab(finalTitle, pgn, startFen, targetDatabase, databaseGameId);
        Tabs.Add(tab);
        ActiveTab = tab;
        NotifyStateChanged();
        return tab;
    }

    public DatabaseBrowserTab CreateDatabaseTab()
    {
        var existing = Tabs.OfType<DatabaseBrowserTab>().FirstOrDefault();
        if (existing != null)
        {
            ActiveTab = existing;
            NotifyStateChanged();
            return existing;
        }

        var tab = new DatabaseBrowserTab();
        Tabs.Add(tab);
        ActiveTab = tab;
        NotifyStateChanged();
        return tab;
    }

    public void SelectTab(Guid id)
    {
        var target = Tabs.FirstOrDefault(t => t.Id == id);
        if (target != null && target != ActiveTab)
        {
            ActiveTab = target;
            NotifyStateChanged();
        }
    }

    public void CloseTab(Guid id)
    {
        var target = Tabs.FirstOrDefault(t => t.Id == id);
        if (target == null || !target.CanClose) return;

        int index = Tabs.IndexOf(target);
        Tabs.Remove(target);

        if (ActiveTab == target)
        {
            int nextIndex = Math.Min(index, Tabs.Count - 1);
            ActiveTab = Tabs[nextIndex];
        }

        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
