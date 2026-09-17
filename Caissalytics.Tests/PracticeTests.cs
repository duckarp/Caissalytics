using Caissalytics.Components;
using Caissalytics.Core;
using Caissalytics.Engine;
using Xunit;

namespace Caissalytics.Tests;

public class PracticeTests
{
    [Fact]
    public void PracticeTab_DefaultInitialization_SetsHeadersAndOrientation()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreatePracticeTab(playerColor: PieceColor.White, opponentElo: 1600);

        Assert.NotNull(tab);
        Assert.Equal(PieceColor.White, tab.PlayerColor);
        Assert.Equal(1600, tab.OpponentElo);
        Assert.Equal("white", tab.Orientation);
        Assert.Equal("Player", tab.Tree.Headers["White"]);
        Assert.Equal("Stockfish Bot (1600)", tab.Tree.Headers["Black"]);
        Assert.Equal(1600.ToString(), tab.Tree.Headers["BlackElo"]);
        Assert.Equal("Practice vs Computer", tab.Tree.Headers["Event"]);
        Assert.Equal("🤖", tab.Icon);
        Assert.Contains("1600", tab.Title);
    }

    [Fact]
    public void PracticeTab_PlayAsBlack_SetsBlackOrientation()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreatePracticeTab(playerColor: PieceColor.Black, opponentElo: 2100);

        Assert.Equal(PieceColor.Black, tab.PlayerColor);
        Assert.Equal(2100, tab.OpponentElo);
        Assert.Equal("black", tab.Orientation);
        Assert.Equal("Stockfish Bot (2100)", tab.Tree.Headers["White"]);
        Assert.Equal(2100.ToString(), tab.Tree.Headers["WhiteElo"]);
        Assert.Equal("Player", tab.Tree.Headers["Black"]);
    }

    [Fact]
    public void PracticeTab_FromCustomPosition_PreservesTreeAndPosition()
    {
        string fen = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3";
        var ws = new WorkspaceState();
        var tab = ws.CreatePracticeTab(startFen: fen, playerColor: PieceColor.White, opponentElo: 1400);

        Assert.Equal(fen, tab.StartFen);
        Assert.Equal(PieceColor.White, tab.Tree.CurrentNode.Position.ActiveColor);
        Assert.False(tab.Tree.CanGoBack);
    }

    [Fact]
    public void PracticeTab_FromExistingAnalysisTree_ClonesMoves()
    {
        var analysisTree = new GameTree();
        analysisTree.AddMoveSan("e4");
        analysisTree.AddMoveSan("e5");
        analysisTree.AddMoveSan("Nf3");

        var ws = new WorkspaceState();
        var tab = ws.CreatePracticeTab(
            startFen: FenParser.ToFen(analysisTree.CurrentNode.Position),
            playerColor: PieceColor.Black,
            opponentElo: 1800,
            existingTree: analysisTree);

        Assert.Equal(PieceColor.Black, tab.PlayerColor);
        Assert.Equal(1800, tab.OpponentElo);
        // Mainline should have the 3 existing moves
        Assert.True(tab.Tree.CanGoBack);
        Assert.Equal("Nf3", tab.Tree.CurrentNode.San);
    }

    [Fact]
    public void WorkspaceState_ExportAndRestore_PracticeTab()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreatePracticeTab(playerColor: PieceColor.Black, opponentElo: 1900);
        tab.Tree.AddMoveSan("e4");
        tab.Tree.AddMoveSan("c5");

        ws.SelectTab(tab.Id);
        string json = ws.ExportStateJson();
        Assert.False(string.IsNullOrWhiteSpace(json));

        var ws2 = new WorkspaceState();
        bool restored = ws2.RestoreStateFromJson(json);

        Assert.True(restored);
        Assert.Equal(2, ws2.Tabs.Count); // Dashboard + Practice

        var restoredPractice = Assert.IsType<PracticeTab>(ws2.ActiveTab);
        Assert.Equal(PieceColor.Black, restoredPractice.PlayerColor);
        Assert.Equal(1900, restoredPractice.OpponentElo);
        Assert.Equal("black", restoredPractice.Orientation);
        Assert.Equal("c5", restoredPractice.Tree.CurrentNode.San);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1300)]
    [InlineData(1600)]
    [InlineData(1900)]
    [InlineData(2200)]
    [InlineData(2500)]
    public void PracticeTab_ValidEloPresets_AreClampedAndFormatted(int elo)
    {
        var tab = new PracticeTab(opponentElo: elo);
        Assert.Equal(elo, tab.OpponentElo);
        Assert.Equal($"Practice ({elo})", tab.Title);
    }

    [Fact]
    public void PracticeTab_GameStartedCheck_IdentifiesMovesPlayed()
    {
        var tab = new PracticeTab();
        // At start position, root has no children
        Assert.Empty(tab.Tree.Root.Children);

        // Making a move starts the game
        tab.Tree.AddMoveSan("e4");
        Assert.NotEmpty(tab.Tree.Root.Children);
        Assert.Single(tab.Tree.Root.Children);
    }
}
