using Caissalytics.Core;
using Xunit;

namespace Caissalytics.Tests;

public class DrawRulesTests
{
    [Theory]
    [InlineData("8/8/4k3/8/8/4K3/8/8 w - - 0 1", true)]          // K v K
    [InlineData("8/8/4k3/8/8/4K3/5N2/8 w - - 0 1", true)]        // K+N v K
    [InlineData("8/8/4k3/8/8/4K3/5B2/8 w - - 0 1", true)]        // K+B v K
    [InlineData("8/8/3bk3/8/8/4K3/5B2/8 w - - 0 1", true)]       // same-coloured bishops (d6 and f2 both dark)
    [InlineData("8/8/2b1k3/8/8/4K3/5B2/8 w - - 0 1", false)]     // opposite-coloured bishops (c6 light, f2 dark)
    [InlineData("8/8/4k3/8/8/4K3/4NN2/8 w - - 0 1", false)]      // two knights: mate possible with help
    [InlineData("8/8/4k3/8/8/4K3/4P3/8 w - - 0 1", false)]       // pawn
    [InlineData("8/8/4k3/8/8/4K3/4R3/8 w - - 0 1", false)]       // rook
    public void InsufficientMaterial(string fen, bool expected)
    {
        Assert.Equal(expected, DrawRules.IsInsufficientMaterial(FenParser.Parse(fen)));
    }

    [Fact]
    public void ThreefoldRepetition_DetectedOnThirdOccurrence()
    {
        var tree = new GameTree();
        string[] shuffle = ["Nf3", "Nf6", "Ng1", "Ng8"];

        foreach (var san in shuffle) tree.AddMoveSan(san);
        Assert.Equal(DrawReason.None, DrawRules.Detect(tree.CurrentNode)); // start position seen twice

        foreach (var san in shuffle) tree.AddMoveSan(san);
        Assert.Equal(DrawReason.ThreefoldRepetition, DrawRules.Detect(tree.CurrentNode));
    }

    [Fact]
    public void FiftyMoveRule_After100QuietPlies()
    {
        var tree = new GameTree("8/8/4k3/8/8/4K3/4R3/8 w - - 99 80");
        Assert.Equal(DrawReason.None, DrawRules.Detect(tree.CurrentNode));

        tree.AddMoveSan("Ra2");
        Assert.Equal(DrawReason.FiftyMoveRule, DrawRules.Detect(tree.CurrentNode));
    }

    [Fact]
    public void GameTree_RecordsInsufficientMaterialButNotClaimableDraws()
    {
        // Kxe2 takes the last piece, leaving K v K
        var bare = new GameTree("8/8/4k3/8/8/4K3/4r3/8 w - - 0 1");
        bare.AddMoveSan("Kxe2");
        Assert.Equal("1/2-1/2", bare.Headers["Result"]);
        Assert.Equal("Insufficient material", bare.Headers["Termination"]);

        // Threefold repetition can be claimed but play may continue: no result is written
        var repeated = new GameTree();
        foreach (var san in new[] { "Nf3", "Nf6", "Ng1", "Ng8", "Nf3", "Nf6", "Ng1", "Ng8" }) repeated.AddMoveSan(san);
        Assert.False(repeated.Headers.TryGetValue("Result", out var result) && result != "*");
    }

    [Fact]
    public void GameTree_MateInASideLine_DoesNotSetTheGameResult()
    {
        // Fool's mate position before 2...Qh4#
        var tree = PgnHandler.ImportPgn("1. f3 e5 2. g4 *");
        tree.GoToEnd();
        tree.AddMoveSan("Nc6");            // main line continues
        tree.NavigateTo(tree.CurrentNode.Parent!);
        tree.AddMoveSan("Qh4#");           // mate explored as a variation

        Assert.Equal("*", tree.Headers["Result"]);
    }
}
