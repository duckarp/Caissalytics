using Caissalytics.Components;
using Caissalytics.Core;
using Xunit;

namespace Caissalytics.Tests;

public class WorkspacePersistenceTests
{
    [Fact]
    public void ExportAndRestore_DashboardOnly()
    {
        var ws = new WorkspaceState();
        string json = ws.ExportStateJson();
        Assert.False(string.IsNullOrWhiteSpace(json));

        var ws2 = new WorkspaceState();
        bool restored = ws2.RestoreStateFromJson(json);

        Assert.True(restored);
        Assert.Single(ws2.Tabs);
        Assert.IsType<DashboardTab>(ws2.ActiveTab);
        Assert.Equal("Dashboard", ws2.ActiveTab.Title);
    }

    [Fact]
    public void ExportAndRestore_MultipleTabs_And_ActiveTab()
    {
        var ws = new WorkspaceState();
        var tab1 = ws.CreateAnalysisTab("Kasparov vs Topalov", targetDatabase: "MasterGames", databaseGameId: 42);
        tab1.Orientation = "black";
        tab1.Tree.AddMoveSan("e4");
        tab1.Tree.AddMoveSan("d6");

        var tab2 = ws.CreateAnalysisTab("Sicilian Defense");
        tab2.Tree.AddMoveSan("e4");
        tab2.Tree.AddMoveSan("c5");

        var dbTab = ws.CreateDatabaseTab();

        // Select tab1 as active
        ws.SelectTab(tab1.Id);
        Assert.Equal(tab1.Id, ws.ActiveTab.Id);

        string json = ws.ExportStateJson();

        var ws2 = new WorkspaceState();
        bool restored = ws2.RestoreStateFromJson(json);

        Assert.True(restored);
        Assert.Equal(4, ws2.Tabs.Count); // Dashboard + 2 Analysis + 1 Database

        Assert.Equal(tab1.Id, ws2.ActiveTab.Id);
        var restoredTab1 = Assert.IsType<AnalysisTab>(ws2.ActiveTab);
        Assert.Equal("Kasparov vs Topalov", restoredTab1.Title);
        Assert.Equal("black", restoredTab1.Orientation);
        Assert.Equal("MasterGames", restoredTab1.TargetDatabase);
        Assert.Equal(42, restoredTab1.DatabaseGameId);
        Assert.Equal("d6", restoredTab1.Tree.CurrentNode.San);

        var restoredTab2 = ws2.Tabs.OfType<AnalysisTab>().FirstOrDefault(t => t.Id == tab2.Id);
        Assert.NotNull(restoredTab2);
        Assert.Equal("Sicilian Defense", restoredTab2.Title);
        Assert.Equal("white", restoredTab2.Orientation);

        var restoredDbTab = ws2.Tabs.OfType<DatabaseBrowserTab>().FirstOrDefault();
        Assert.NotNull(restoredDbTab);
        Assert.Equal(dbTab.Id, restoredDbTab.Id);
    }

    [Fact]
    public void ExportAndRestore_TreeCurrentNodePath_MainlineAndVariation()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreateAnalysisTab("Variation Test");
        
        // Mainline: 1. e4 e5 2. Nf3
        tab.Tree.AddMoveSan("e4");
        var e5Node = tab.Tree.AddMoveSan("e5");
        tab.Tree.AddMoveSan("Nf3");

        // Variation on move 1: 1... c5 2. Nf3 d6
        tab.Tree.NavigateTo(tab.Tree.Root.Children[0]); // at 1. e4
        tab.Tree.AddMoveSan("c5");
        tab.Tree.AddMoveSan("Nf3");
        var d6Node = tab.Tree.AddMoveSan("d6");

        // User is currently analyzing 2... d6
        Assert.Equal("d6", tab.Tree.CurrentNode.San);
        var path = tab.Tree.GetCurrentNodePath();
        Assert.NotEmpty(path);

        string json = ws.ExportStateJson();

        var ws2 = new WorkspaceState();
        bool restored = ws2.RestoreStateFromJson(json);

        Assert.True(restored);
        var restoredTab = Assert.IsType<AnalysisTab>(ws2.ActiveTab);
        Assert.Equal("d6", restoredTab.Tree.CurrentNode.San);
        Assert.Equal(2, restoredTab.Tree.CurrentNode.MoveNumber);
        Assert.False(restoredTab.Tree.CurrentNode.IsWhiteMove);
    }

    [Fact]
    public void ExportAndRestore_CustomFenStudy()
    {
        // Custom endgame FEN: White King + Pawn vs Black King
        string customFen = "8/8/8/4k3/8/8/4P3/4K3 w - - 0 1";
        var ws = new WorkspaceState();
        var tab = ws.CreateAnalysisTab("Endgame Study", startFen: customFen);
        tab.Tree.AddMoveSan("e4");
        tab.Tree.AddMoveSan("Ke6");

        string json = ws.ExportStateJson();

        var ws2 = new WorkspaceState();
        bool restored = ws2.RestoreStateFromJson(json);

        Assert.True(restored);
        var restoredTab = Assert.IsType<AnalysisTab>(ws2.ActiveTab);
        Assert.Equal("Endgame Study", restoredTab.Title);
        Assert.Equal(customFen, FenParser.ToFen(restoredTab.Tree.Root.Position));
        Assert.Equal("Ke6", restoredTab.Tree.CurrentNode.San);
    }

    [Fact]
    public void Restore_CorruptOrEmptyJson_GracefulFallback()
    {
        var ws = new WorkspaceState();

        Assert.False(ws.RestoreStateFromJson(null));
        Assert.False(ws.RestoreStateFromJson(""));
        Assert.False(ws.RestoreStateFromJson("   "));
        Assert.False(ws.RestoreStateFromJson("{ not valid json }"));
        Assert.False(ws.RestoreStateFromJson("{\"Tabs\": []}"));

        // Workspace should remain intact with Dashboard
        Assert.Single(ws.Tabs);
        Assert.IsType<DashboardTab>(ws.ActiveTab);
    }

    [Fact]
    public void AnalysisTab_Mutations_Trigger_WorkspaceOnChange()
    {
        var ws = new WorkspaceState();
        int changeCount = 0;
        ws.OnChange += () => changeCount++;

        var tab = ws.CreateAnalysisTab("Reactive Tab");
        int countAfterCreate = changeCount;
        Assert.True(countAfterCreate > 0);

        // Making a move should trigger OnChange
        tab.Tree.AddMoveSan("e4");
        Assert.True(changeCount > countAfterCreate);

        // Changing orientation should trigger OnChange
        int countBeforeOrient = changeCount;
        tab.Orientation = "black";
        Assert.True(changeCount > countBeforeOrient);

        // Changing title should trigger OnChange
        int countBeforeTitle = changeCount;
        tab.SetTitle("Updated Title");
        Assert.True(changeCount > countBeforeTitle);
    }
}
