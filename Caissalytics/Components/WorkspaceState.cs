using System.Text.Json;
using Caissalytics.Core;
using Caissalytics.Engine;

namespace Caissalytics.Components;

public abstract class WorkspaceTab
{
    public Guid Id { get; set; } = Guid.NewGuid();
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

    public event Action? OnTabChanged;

    private GameTree _tree = default!;
    public GameTree Tree
    {
        get => _tree;
        set
        {
            if (_tree != null)
            {
                _tree.PositionChanged -= HandlePositionChanged;
            }
            _tree = value;
            if (_tree != null)
            {
                _tree.PositionChanged += HandlePositionChanged;
            }
            NotifyTabChanged();
        }
    }

    private string _orientation = "white";
    public string Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation != value)
            {
                _orientation = value;
                NotifyTabChanged();
            }
        }
    }

    private bool _isAnalyzing = false;
    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        set
        {
            if (_isAnalyzing != value)
            {
                _isAnalyzing = value;
                NotifyTabChanged();
            }
        }
    }

    private int _multiPv = 3;
    public int MultiPv
    {
        get => _multiPv;
        set
        {
            if (_multiPv != value)
            {
                _multiPv = value;
                NotifyTabChanged();
            }
        }
    }

    public List<EngineEvaluationLine> CurrentEvalLines { get; set; } = new();

    private string? _targetDatabase;
    public string? TargetDatabase
    {
        get => _targetDatabase;
        set
        {
            if (_targetDatabase != value)
            {
                _targetDatabase = value;
                NotifyTabChanged();
            }
        }
    }

    private long? _databaseGameId;
    public long? DatabaseGameId
    {
        get => _databaseGameId;
        set
        {
            if (_databaseGameId != value)
            {
                _databaseGameId = value;
                NotifyTabChanged();
            }
        }
    }

    private GameAnalysisReport? _analysisReport;
    public GameAnalysisReport? AnalysisReport
    {
        get => _analysisReport;
        set
        {
            if (_analysisReport != value)
            {
                _analysisReport = value;
                NotifyTabChanged();
            }
        }
    }

    public AnalysisTab(string? title = null, string? pgn = null, string? startFen = null, string? targetDatabase = null, long? databaseGameId = null)
    {
        _customTitle = title ?? "Analysis Board";
        _targetDatabase = targetDatabase;
        _databaseGameId = databaseGameId;
        Tree = !string.IsNullOrWhiteSpace(pgn)
            ? PgnHandler.ImportPgn(pgn)
            : new GameTree(startFen);
    }

    public void SetTitle(string title)
    {
        if (_customTitle != title)
        {
            _customTitle = title;
            NotifyTabChanged();
        }
    }

    private void HandlePositionChanged() => NotifyTabChanged();

    public void NotifyTabChanged() => OnTabChanged?.Invoke();
}

public class DatabaseBrowserTab : WorkspaceTab
{
    public override string Title => "Database";
    public override string Icon => "🗄️";
    public string? TargetDatabase { get; set; }

    public DatabaseBrowserTab(string? targetDatabase = null)
    {
        TargetDatabase = targetDatabase;
    }
}

public class SettingsTab : WorkspaceTab
{
    public override string Title => "Control Center";
    public override string Icon => "⚙️";
    public string? InitialCategory { get; set; }

    public SettingsTab(string? initialCategory = null)
    {
        InitialCategory = initialCategory;
    }
}

public class AnalyticsTab : WorkspaceTab
{
    public override string Title => "Personal Insights";
    public override string Icon => "📊";
    public string? DatabaseScope { get; set; }

    public AnalyticsTab(string? databaseScope = null)
    {
        DatabaseScope = databaseScope;
    }
}

public class PuzzleTrainerTab : WorkspaceTab
{
    public override string Title => "Puzzle Trainer";
    public override string Icon => "🧩";
    public string? InitialMode { get; set; }

    public PuzzleTrainerTab(string? initialMode = null)
    {
        InitialMode = initialMode;
    }
}

public class RepertoireExplorerTab : WorkspaceTab
{
    public override string Title => "Opening Tree";
    public override string Icon => "📖";
    public string? DatabaseScope { get; set; }

    public RepertoireExplorerTab(string? databaseScope = null)
    {
        DatabaseScope = databaseScope;
    }
}

public class OpponentDossierTab : WorkspaceTab
{
    public override string Title => "Opponent Prep";
    public override string Icon => "🕵️‍♂️";
    public string? TargetPlayer { get; set; }

    public OpponentDossierTab(string? targetPlayer = null)
    {
        TargetPlayer = targetPlayer;
    }
}

public class EndgameTrainerTab : WorkspaceTab
{
    public override string Title => "Endgames";
    public override string Icon => "🏆";
    public string? SelectedPositionId { get; set; }

    public EndgameTrainerTab(string? selectedPositionId = null)
    {
        SelectedPositionId = selectedPositionId;
    }
}

public class HomeworkTab : WorkspaceTab
{
    public override string Title => "Homework & Diagrams";
    public override string Icon => "📝";
    public string? InitialSheetId { get; set; }

    public HomeworkTab(string? initialSheetId = null)
    {
        InitialSheetId = initialSheetId;
    }
}

public class WorkspaceState
{
    public List<WorkspaceTab> Tabs { get; } = new();
    public WorkspaceTab ActiveTab { get; private set; }
    public event Action? OnChange;

    private bool _isRestoring = false;

    public WorkspaceState()
    {
        var dashboard = new DashboardTab();
        Tabs.Add(dashboard);
        ActiveTab = dashboard;
    }

    private void RegisterTab(WorkspaceTab tab)
    {
        if (tab is AnalysisTab analysis)
        {
            analysis.OnTabChanged += OnTabStateChanged;
        }
    }

    private void UnregisterTab(WorkspaceTab tab)
    {
        if (tab is AnalysisTab analysis)
        {
            analysis.OnTabChanged -= OnTabStateChanged;
        }
    }

    private void OnTabStateChanged()
    {
        if (!_isRestoring)
        {
            NotifyStateChanged();
        }
    }

    public AnalysisTab CreateAnalysisTab(string? title = null, string? pgn = null, string? startFen = null, string? targetDatabase = null, long? databaseGameId = null)
    {
        int analysisCount = Tabs.OfType<AnalysisTab>().Count() + 1;
        string finalTitle = title ?? $"Analysis {analysisCount}";

        var tab = new AnalysisTab(finalTitle, pgn, startFen, targetDatabase, databaseGameId);
        RegisterTab(tab);
        Tabs.Add(tab);
        ActiveTab = tab;
        NotifyStateChanged();
        return tab;
    }

    public DatabaseBrowserTab CreateDatabaseTab(string? databaseName = null)
    {
        var existing = Tabs.OfType<DatabaseBrowserTab>().FirstOrDefault();
        if (existing != null)
        {
            if (!string.IsNullOrEmpty(databaseName))
            {
                existing.TargetDatabase = databaseName;
            }
            ActiveTab = existing;
            NotifyStateChanged();
            return existing;
        }

        var tab = new DatabaseBrowserTab(databaseName);
        Tabs.Add(tab);
        ActiveTab = tab;
        NotifyStateChanged();
        return tab;
    }

    public SettingsTab CreateSettingsTab(string? category = null)
    {
        var existing = Tabs.OfType<SettingsTab>().FirstOrDefault();
        if (existing != null)
        {
            if (!string.IsNullOrEmpty(category))
            {
                existing.InitialCategory = category;
            }
            ActiveTab = existing;
            NotifyStateChanged();
            return existing;
        }

        var tab = new SettingsTab(category);
        Tabs.Add(tab);
        ActiveTab = tab;
        NotifyStateChanged();
        return tab;
    }

    public AnalyticsTab CreateAnalyticsTab(string? databaseScope = null)
    {
        var existing = Tabs.OfType<AnalyticsTab>().FirstOrDefault();
        if (existing != null)
        {
            if (!string.IsNullOrEmpty(databaseScope))
            {
                existing.DatabaseScope = databaseScope;
            }
            ActiveTab = existing;
            NotifyStateChanged();
            return existing;
        }

        var tab = new AnalyticsTab(databaseScope);
        Tabs.Add(tab);
        ActiveTab = tab;
        NotifyStateChanged();
        return tab;
    }

    public PuzzleTrainerTab CreatePuzzleTrainerTab(string? initialMode = null)
    {
        var existing = Tabs.OfType<PuzzleTrainerTab>().FirstOrDefault();
        if (existing != null)
        {
            if (!string.IsNullOrEmpty(initialMode))
            {
                existing.InitialMode = initialMode;
            }
            ActiveTab = existing;
            NotifyStateChanged();
            return existing;
        }

        var tab = new PuzzleTrainerTab(initialMode);
        Tabs.Add(tab);
        ActiveTab = tab;
        NotifyStateChanged();
        return tab;
    }

    public RepertoireExplorerTab CreateRepertoireTab(string? databaseScope = null)
    {
        var existing = Tabs.OfType<RepertoireExplorerTab>().FirstOrDefault();
        if (existing != null)
        {
            if (!string.IsNullOrEmpty(databaseScope))
            {
                existing.DatabaseScope = databaseScope;
            }
            ActiveTab = existing;
            NotifyStateChanged();
            return existing;
        }

        var tab = new RepertoireExplorerTab(databaseScope);
        Tabs.Add(tab);
        ActiveTab = tab;
        NotifyStateChanged();
        return tab;
    }

    public OpponentDossierTab CreateOpponentDossierTab(string? targetPlayer = null)
    {
        var existing = Tabs.OfType<OpponentDossierTab>().FirstOrDefault();
        if (existing != null)
        {
            if (!string.IsNullOrEmpty(targetPlayer))
            {
                existing.TargetPlayer = targetPlayer;
            }
            ActiveTab = existing;
            NotifyStateChanged();
            return existing;
        }

        var tab = new OpponentDossierTab(targetPlayer);
        Tabs.Add(tab);
        ActiveTab = tab;
        NotifyStateChanged();
        return tab;
    }

    public EndgameTrainerTab CreateEndgameTrainerTab(string? positionId = null)
    {
        var existing = Tabs.OfType<EndgameTrainerTab>().FirstOrDefault();
        if (existing != null)
        {
            if (!string.IsNullOrEmpty(positionId))
            {
                existing.SelectedPositionId = positionId;
            }
            ActiveTab = existing;
            NotifyStateChanged();
            return existing;
        }

        var tab = new EndgameTrainerTab(positionId);
        Tabs.Add(tab);
        ActiveTab = tab;
        NotifyStateChanged();
        return tab;
    }

    public HomeworkTab CreateHomeworkTab(string? sheetId = null)
    {
        var existing = Tabs.OfType<HomeworkTab>().FirstOrDefault();
        if (existing != null)
        {
            if (!string.IsNullOrEmpty(sheetId))
            {
                existing.InitialSheetId = sheetId;
            }
            ActiveTab = existing;
            NotifyStateChanged();
            return existing;
        }

        var tab = new HomeworkTab(sheetId);
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

        UnregisterTab(target);
        int index = Tabs.IndexOf(target);
        Tabs.Remove(target);

        if (ActiveTab == target)
        {
            int nextIndex = Math.Min(index, Tabs.Count - 1);
            ActiveTab = Tabs[nextIndex];
        }

        NotifyStateChanged();
    }

    public string ExportStateJson()
    {
        var dto = new WorkspaceStateDto
        {
            ActiveTabId = ActiveTab?.Id ?? Guid.Empty,
            Tabs = new List<WorkspaceTabDto>()
        };

        foreach (var tab in Tabs)
        {
            switch (tab)
            {
                case DashboardTab dash:
                    dto.Tabs.Add(new WorkspaceTabDto
                    {
                        Id = dash.Id,
                        Type = "dashboard",
                        Title = dash.Title
                    });
                    break;

                case AnalysisTab analysis:
                    dto.Tabs.Add(new WorkspaceTabDto
                    {
                        Id = analysis.Id,
                        Type = "analysis",
                        Title = analysis.Title,
                        Pgn = PgnHandler.ExportPgn(analysis.Tree),
                        CurrentNodePath = analysis.Tree.GetCurrentNodePath(),
                        Orientation = analysis.Orientation,
                        IsAnalyzing = analysis.IsAnalyzing,
                        MultiPv = analysis.MultiPv,
                        TargetDatabase = analysis.TargetDatabase,
                        DatabaseGameId = analysis.DatabaseGameId,
                        AnalysisReport = analysis.AnalysisReport
                    });
                    break;

                case DatabaseBrowserTab db:
                    dto.Tabs.Add(new WorkspaceTabDto
                    {
                        Id = db.Id,
                        Type = "database",
                        Title = db.Title
                    });
                    break;

                case SettingsTab settings:
                    dto.Tabs.Add(new WorkspaceTabDto
                    {
                        Id = settings.Id,
                        Type = "settings",
                        Title = settings.Title
                    });
                    break;

                case AnalyticsTab analytics:
                    dto.Tabs.Add(new WorkspaceTabDto
                    {
                        Id = analytics.Id,
                        Type = "analytics",
                        Title = analytics.Title,
                        DatabaseScope = analytics.DatabaseScope
                    });
                    break;

                case PuzzleTrainerTab puzzles:
                    dto.Tabs.Add(new WorkspaceTabDto
                    {
                        Id = puzzles.Id,
                        Type = "puzzles",
                        Title = puzzles.Title
                    });
                    break;

                case RepertoireExplorerTab repertoire:
                    dto.Tabs.Add(new WorkspaceTabDto
                    {
                        Id = repertoire.Id,
                        Type = "repertoire",
                        Title = repertoire.Title,
                        DatabaseScope = repertoire.DatabaseScope
                    });
                    break;

                case OpponentDossierTab dossier:
                    dto.Tabs.Add(new WorkspaceTabDto
                    {
                        Id = dossier.Id,
                        Type = "dossier",
                        Title = dossier.Title,
                        Player = dossier.TargetPlayer
                    });
                    break;

                case EndgameTrainerTab endgame:
                    dto.Tabs.Add(new WorkspaceTabDto
                    {
                        Id = endgame.Id,
                        Type = "endgames",
                        Title = endgame.Title,
                        PositionId = endgame.SelectedPositionId
                    });
                    break;

                case HomeworkTab hw:
                    dto.Tabs.Add(new WorkspaceTabDto
                    {
                        Id = hw.Id,
                        Type = "homework",
                        Title = hw.Title
                    });
                    break;
            }
        }

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }

    public bool RestoreStateFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            var dto = JsonSerializer.Deserialize<WorkspaceStateDto>(json);
            if (dto == null || dto.Tabs == null || dto.Tabs.Count == 0)
                return false;

            _isRestoring = true;
            try
            {
                foreach (var tab in Tabs)
                {
                    UnregisterTab(tab);
                }
                Tabs.Clear();

                foreach (var tabDto in dto.Tabs)
                {
                    if (tabDto.Type == "dashboard")
                    {
                        var dash = new DashboardTab { Id = tabDto.Id };
                        Tabs.Add(dash);
                    }
                    else if (tabDto.Type == "analysis")
                    {
                        var analysisTab = new AnalysisTab(
                            title: tabDto.Title,
                            pgn: tabDto.Pgn,
                            targetDatabase: tabDto.TargetDatabase,
                            databaseGameId: tabDto.DatabaseGameId)
                        {
                            Id = tabDto.Id,
                            Orientation = tabDto.Orientation ?? "white",
                            IsAnalyzing = tabDto.IsAnalyzing,
                            MultiPv = tabDto.MultiPv > 0 ? tabDto.MultiPv : 3,
                            AnalysisReport = tabDto.AnalysisReport
                        };

                        if (tabDto.CurrentNodePath != null && tabDto.CurrentNodePath.Count > 0)
                        {
                            analysisTab.Tree.NavigatePath(tabDto.CurrentNodePath);
                        }

                        RegisterTab(analysisTab);
                        Tabs.Add(analysisTab);
                    }
                    else if (tabDto.Type == "database")
                    {
                        var dbTab = new DatabaseBrowserTab { Id = tabDto.Id };
                        Tabs.Add(dbTab);
                    }
                    else if (tabDto.Type == "settings")
                    {
                        var settingsTab = new SettingsTab { Id = tabDto.Id };
                        Tabs.Add(settingsTab);
                    }
                    else if (tabDto.Type == "analytics")
                    {
                        var analyticsTab = new AnalyticsTab(tabDto.DatabaseScope) { Id = tabDto.Id };
                        Tabs.Add(analyticsTab);
                    }
                    else if (tabDto.Type == "puzzles")
                    {
                        var puzzlesTab = new PuzzleTrainerTab { Id = tabDto.Id };
                        Tabs.Add(puzzlesTab);
                    }
                    else if (tabDto.Type == "repertoire")
                    {
                        var repertoireTab = new RepertoireExplorerTab(tabDto.DatabaseScope) { Id = tabDto.Id };
                        Tabs.Add(repertoireTab);
                    }
                    else if (tabDto.Type == "dossier")
                    {
                        var dossierTab = new OpponentDossierTab(tabDto.Player) { Id = tabDto.Id };
                        Tabs.Add(dossierTab);
                    }
                    else if (tabDto.Type == "endgames")
                    {
                        var endgamesTab = new EndgameTrainerTab(tabDto.PositionId) { Id = tabDto.Id };
                        Tabs.Add(endgamesTab);
                    }
                    else if (tabDto.Type == "homework")
                    {
                        var hwTab = new HomeworkTab { Id = tabDto.Id };
                        Tabs.Add(hwTab);
                    }
                }

                if (!Tabs.Any(t => t is DashboardTab))
                {
                    var dash = new DashboardTab();
                    Tabs.Insert(0, dash);
                }

                var active = Tabs.FirstOrDefault(t => t.Id == dto.ActiveTabId);
                ActiveTab = active ?? Tabs.First();
            }
            finally
            {
                _isRestoring = false;
            }

            OnChange?.Invoke();
            return true;
        }
        catch
        {
            _isRestoring = false;
            return false;
        }
    }

    private void NotifyStateChanged()
    {
        if (!_isRestoring)
        {
            OnChange?.Invoke();
        }
    }
}
