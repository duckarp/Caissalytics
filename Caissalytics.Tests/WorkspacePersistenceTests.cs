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

    [Fact]
    public void CreateDatabaseTab_WithTargetDatabase_SetsTargetDatabase()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreateDatabaseTab("My online games");

        Assert.Equal("My online games", tab.TargetDatabase);
        Assert.Equal(tab, ws.ActiveTab);

        // Re-invoking with another database updates existing tab's target database and activates it
        var tab2 = ws.CreateDatabaseTab("ClassicalMasters");
        Assert.Same(tab, tab2);
        Assert.Equal("ClassicalMasters", tab2.TargetDatabase);
        Assert.Equal(tab2, ws.ActiveTab);
    }

    [Fact]
    public void CreateSettingsTab_WithInitialCategory_SetsInitialCategory()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreateSettingsTab("sync");

        Assert.Equal("sync", tab.InitialCategory);
        Assert.Equal(tab, ws.ActiveTab);

        // Re-invoking with another category updates existing tab's initial category
        var tab2 = ws.CreateSettingsTab("engines");
        Assert.Same(tab, tab2);
        Assert.Equal("engines", tab2.InitialCategory);
        Assert.Equal(tab2, ws.ActiveTab);
    }

    [Fact]
    public void CreateClubHomeworkTab_WithInitialTaskId_SetsTaskIdAndActivates()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreateClubHomeworkTab(123);

        Assert.Equal(123, tab.InitialTaskId);
        Assert.Equal(tab, ws.ActiveTab);
        Assert.Equal("Club Homework", tab.Title);
        Assert.Equal("📥", tab.Icon);

        // Re-invoking updates existing tab's initial task ID
        var tab2 = ws.CreateClubHomeworkTab(456);
        Assert.Same(tab, tab2);
        Assert.Equal(456, tab2.InitialTaskId);
        Assert.Equal(tab2, ws.ActiveTab);
    }

    [Fact]
    public void ExportAndRestore_HomeworkAndClubHomeworkTabs()
    {
        var ws = new WorkspaceState();
        var hwTab = ws.CreateHomeworkTab("sheet-1");
        var clubTab = ws.CreateClubHomeworkTab(42);

        ws.SelectTab(clubTab.Id);

        string json = ws.ExportStateJson();
        Assert.False(string.IsNullOrWhiteSpace(json));

        var ws2 = new WorkspaceState();
        bool restored = ws2.RestoreStateFromJson(json);

        Assert.True(restored);
        Assert.Equal(3, ws2.Tabs.Count); // Dashboard + Homework + ClubHomework

        var restoredClub = Assert.IsType<ClubHomeworkTab>(ws2.ActiveTab);
        Assert.Equal(clubTab.Id, restoredClub.Id);
        Assert.Equal("Club Homework", restoredClub.Title);

        var restoredHw = ws2.Tabs.OfType<HomeworkTab>().FirstOrDefault();
        Assert.NotNull(restoredHw);
        Assert.Equal(hwTab.Id, restoredHw.Id);
        Assert.Equal("Homework & Diagrams", restoredHw.Title);
    }

    [Fact]
    public void CreateClubMessagesTab_WithInitialMessageId_SetsMessageIdAndActivates()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreateMessagesTab(77);

        Assert.Equal(77, tab.InitialMessageId);
        Assert.Equal(tab, ws.ActiveTab);
        Assert.Equal("Club Messages", tab.Title);
        Assert.Equal("✉️", tab.Icon);

        // Re-invoking updates existing tab's initial message ID
        var tab2 = ws.CreateMessagesTab(88);
        Assert.Same(tab, tab2);
        Assert.Equal(88, tab2.InitialMessageId);
        Assert.Equal(tab2, ws.ActiveTab);
    }

    [Fact]
    public void ExportAndRestore_ClubMessagesTab()
    {
        var ws = new WorkspaceState();
        var msgTab = ws.CreateMessagesTab(5);

        ws.SelectTab(msgTab.Id);

        string json = ws.ExportStateJson();
        Assert.False(string.IsNullOrWhiteSpace(json));

        var ws2 = new WorkspaceState();
        bool restored = ws2.RestoreStateFromJson(json);

        Assert.True(restored);
        Assert.Equal(2, ws2.Tabs.Count); // Dashboard + Messages

        var restoredMsg = Assert.IsType<ClubMessagesTab>(ws2.ActiveTab);
        Assert.Equal(msgTab.Id, restoredMsg.Id);
        Assert.Equal("Club Messages", restoredMsg.Title);
        Assert.Equal("✉️", restoredMsg.Icon);
    }
}
