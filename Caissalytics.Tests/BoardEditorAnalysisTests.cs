using Caissalytics.Components;
using Caissalytics.Core;
using Xunit;

namespace Caissalytics.Tests;

public class BoardEditorAnalysisTests
{
    private const string CustomEndgameFen = "8/5k2/8/8/8/8/4K3/4R3 w - - 0 1";
    private const string CustomTacticalFen = "r1bqk2r/pppp1ppp/2n5/4p3/2B1n3/2P2N2/PPP2PPP/R1BQK2R w KQkq - 0 6";

    [Fact]
    public void AnalysisTab_CustomFen_InitializesCorrectly()
    {
        var tab = new AnalysisTab("Custom Position", startFen: CustomEndgameFen);

        Assert.Equal("Custom Position", tab.Title);
        Assert.NotNull(tab.Tree);
        Assert.Equal("1", tab.Tree.Headers.GetValueOrDefault("SetUp", ""));
        Assert.Equal(CustomEndgameFen, tab.Tree.Headers.GetValueOrDefault("FEN", ""));

        // Verify root position matches custom FEN
        var currentFen = FenParser.ToFen(tab.Tree.CurrentNode.Position);
        Assert.Equal(CustomEndgameFen, currentFen);

        // Verify root has no parent and moves can be played
        Assert.False(tab.Tree.CanGoBack);
        Assert.False(tab.Tree.CanGoForward);
    }

    [Fact]
    public void AnalysisTab_ReplaceWithEditedPosition_UpdatesTreeAndFiresEvents()
    {
        var tab = new AnalysisTab("Initial Game");
        tab.Tree.AddMoveSan("e4");
        tab.Tree.AddMoveSan("e5");
        Assert.Equal("e5", tab.Tree.CurrentNode.San);

        // Simulate user editing the current position
        var newTree = new GameTree(CustomTacticalFen);
        bool positionChangedFired = false;

        tab.Tree.PositionChanged -= () => { };
        tab.Tree = newTree;
        tab.Tree.PositionChanged += () => positionChangedFired = true;

        tab.SetTitle("Tactics Practice");

        Assert.Equal("Tactics Practice", tab.Title);
        Assert.Equal("1", tab.Tree.Headers.GetValueOrDefault("SetUp", ""));
        Assert.Equal(CustomTacticalFen, tab.Tree.Headers.GetValueOrDefault("FEN", ""));

        // Verify position
        var currentFen = FenParser.ToFen(tab.Tree.CurrentNode.Position);
        Assert.Equal(CustomTacticalFen, currentFen);

        // Make move on new position
        var moveNode = tab.Tree.AddMoveSan("Bxf7+");
        Assert.NotNull(moveNode);
        Assert.True(positionChangedFired);
        Assert.Equal("Bxf7+", moveNode.San);
    }

    [Fact]
    public void WorkspaceState_CreateAnalysisTab_WithCustomFen_CreatesAndActivatesTab()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreateAnalysisTab("Edited Position Tab", startFen: CustomEndgameFen);

        Assert.NotNull(tab);
        Assert.Same(tab, ws.ActiveTab);
        Assert.Equal("Edited Position Tab", tab.Title);
        Assert.Equal(CustomEndgameFen, FenParser.ToFen(tab.Tree.CurrentNode.Position));
    }

    [Fact]
    public void BoardPosition_FenParser_Roundtrip_WithCustomPiecePlacements()
    {
        var parsed = FenParser.Parse(CustomTacticalFen);
        var fen = FenParser.ToFen(parsed);

        Assert.Equal(CustomTacticalFen, fen);
    }
}
