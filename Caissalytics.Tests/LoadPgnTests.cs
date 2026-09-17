using Caissalytics.Core;
using Caissalytics.Data;
using Caissalytics.Components;
using Xunit;

namespace Caissalytics.Tests;

public class LoadPgnTests
{
    private const string SamplePgn = @"[Event ""World Championship 34th""]
[Site ""London""]
[Date ""1993.09.07""]
[Round ""1""]
[White ""Kasparov, Garry""]
[Black ""Short, Nigel D""]
[Result ""1-0""]
[WhiteElo ""2815""]
[BlackElo ""2665""]

1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 6. Re1 b5 7. Bb3 d6 8. c3 O-O 9. h3 Nb8 10. d4 Nbd7 1-0";

    [Fact]
    public void LoadPgn_ValidPgn_PopulatesTreeAndHeaders()
    {
        var tree = PgnHandler.ImportPgn(SamplePgn);

        Assert.NotNull(tree);
        Assert.Equal("Kasparov, Garry", tree.Headers["White"]);
        Assert.Equal("Short, Nigel D", tree.Headers["Black"]);
        Assert.Equal("1-0", tree.Headers["Result"]);
        Assert.Equal("London", tree.Headers["Site"]);

        // Verify root has first move (1. e4)
        Assert.True(tree.CanGoForward);
        Assert.True(tree.CurrentNode.IsRoot);
        Assert.NotEmpty(tree.Root.Children);
        Assert.Equal("e4", tree.Root.Children[0].San);

        // Advance through moves
        int moveCount = 0;
        while (tree.CanGoForward)
        {
            tree.GoForward();
            moveCount++;
        }

        Assert.Equal(20, moveCount); // 10 full moves = 20 half-moves
        Assert.False(tree.CanGoForward);
        Assert.True(tree.CanGoBack);
    }

    [Fact]
    public void LoadPgn_WithVariationsAndComments_Works()
    {
        string pgnWithVariations = @"[White ""Player 1""]
[Black ""Player 2""]

1. e4 e5 (1... c5 2. Nf3 {Sicilian Defense}) 2. Nf3 Nc6 *";

        var tree = PgnHandler.ImportPgn(pgnWithVariations);

        Assert.NotNull(tree);
        Assert.True(tree.CanGoForward);
        var e4Node = tree.Root.Children[0];
        Assert.Equal("e4", e4Node.San);

        // e4 should have e5 as main move and c5 as variation
        Assert.Equal(2, e4Node.Children.Count);
        Assert.Equal("e5", e4Node.Children[0].San);
        Assert.Equal("c5", e4Node.Children[1].San);
        Assert.Contains("Sicilian Defense", e4Node.Children[1].Children[0].Comment);
    }

    [Fact]
    public void LoadPgn_AnalysisTabReplacement_UpdatesTreeWithoutDb()
    {
        // 1. Create a fresh empty analysis tab
        var tab = new AnalysisTab("Initial Tab");
        Assert.Equal("Initial Tab", tab.Title);
        Assert.Null(tab.DatabaseGameId);
        Assert.Empty(tab.Tree.Root.Children);

        // 2. Load PGN into tree
        var importedTree = PgnHandler.ImportPgn(SamplePgn);
        tab.Tree = importedTree;
        tab.DatabaseGameId = null;

        string white = tab.Tree.Headers.GetValueOrDefault("White", "");
        string black = tab.Tree.Headers.GetValueOrDefault("Black", "");
        tab.SetTitle($"{white} vs {black}");

        // 3. Verify tab state
        Assert.Equal("Kasparov, Garry vs Short, Nigel D", tab.Title);
        Assert.Null(tab.DatabaseGameId);
        Assert.NotEmpty(tab.Tree.Root.Children);
        Assert.Equal("e4", tab.Tree.Root.Children[0].San);

        // 4. Verify position change event fires on imported tree
        bool positionChangedFired = false;
        tab.Tree.PositionChanged += () => positionChangedFired = true;

        tab.Tree.GoForward();
        Assert.True(positionChangedFired);
        Assert.Equal("e4", tab.Tree.CurrentNode.San);
    }

    [Fact]
    public void LoadPgn_CustomStartFen_ParsedCorrectly()
    {
        string fenPgn = @"[Event ""Tactics""]
[FEN ""r1bqkb1r/pppp1ppp/2n5/4p3/4n3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 4""]

4. Qe2 d5 5. d3 *";

        var tree = PgnHandler.ImportPgn(fenPgn);

        Assert.NotNull(tree);
        Assert.True(tree.CanGoForward);
        Assert.Equal(PieceColor.White, tree.CurrentNode.Position.ActiveColor);
        Assert.Equal("Qe2", tree.Root.Children[0].San);
    }

    [Fact]
    public void Eco_ClassifiesFromBoardMoves()
    {
        var tree = new GameTree();
        tree.AddMoveSan("e4");
        tree.AddMoveSan("c5");
        tree.AddMoveSan("Nf3");
        tree.AddMoveSan("d6");
        tree.AddMoveSan("d4");
        tree.AddMoveSan("cxd4");
        tree.AddMoveSan("Nxd4");
        tree.AddMoveSan("Nf6");
        tree.AddMoveSan("Nc3");
        tree.AddMoveSan("a6");

        var mainline = new List<string>();
        var cur = tree.Root;
        while (cur.Children.Count > 0)
        {
            cur = cur.Children[0];
            mainline.Add(cur.San);
        }

        string? eco = EcoClassifier.Classify(mainline);
        Assert.Equal("B90", eco);
    }

    [Fact]
    public void GameTree_NewTree_HasNoDefaultRound()
    {
        var tree = new GameTree();
        Assert.False(tree.Headers.ContainsKey("Round"));
    }
}
