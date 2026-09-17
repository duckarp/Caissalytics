using Caissalytics.Core;
using Xunit;

namespace Caissalytics.Tests;

public class GameResultHelperTests
{
    [Fact]
    public void GetResult_NullOrEmptyTree_ReturnsNoResult()
    {
        var result = GameResultHelper.GetResult(null);
        Assert.False(result.HasResult);
        Assert.Equal("", result.Score);
    }

    [Fact]
    public void GetResult_WhiteWinsFromHeader_ReturnsWhiteResult()
    {
        var tree = new GameTree();
        tree.Headers["Result"] = "1-0";
        tree.Headers["Termination"] = "White won by resignation";

        var result = GameResultHelper.GetResult(tree);
        Assert.True(result.HasResult);
        Assert.Equal("1-0", result.Score);
        Assert.Equal("1-0", result.DisplayScore);
        Assert.Equal("White won by resignation", result.Description);
        Assert.Equal("result-white-win", result.ResultClass);
    }

    [Fact]
    public void GetResult_BlackWinsFromHeader_ReturnsBlackResult()
    {
        var tree = new GameTree();
        tree.Headers["Result"] = "0-1";

        var result = GameResultHelper.GetResult(tree);
        Assert.True(result.HasResult);
        Assert.Equal("0-1", result.Score);
        Assert.Equal("0-1", result.DisplayScore);
        Assert.Equal("Black is victorious", result.Description);
        Assert.Equal("result-black-win", result.ResultClass);
    }

    [Fact]
    public void GetResult_DrawFromHeader_FormatsFractionCorrectly()
    {
        var tree = new GameTree();
        tree.Headers["Result"] = "1/2-1/2";

        var result = GameResultHelper.GetResult(tree);
        Assert.True(result.HasResult);
        Assert.Equal("1/2-1/2", result.Score);
        Assert.Equal("½-½", result.DisplayScore);
        Assert.Equal("Draw", result.Description);
        Assert.Equal("result-draw", result.ResultClass);
    }

    [Fact]
    public void GetResult_DrawWithSpacedHeader_HandlesCleanly()
    {
        var tree = new GameTree();
        tree.Headers["Result"] = "1/2 - 1/2";

        var result = GameResultHelper.GetResult(tree);
        Assert.True(result.HasResult);
        Assert.Equal("½-½", result.DisplayScore);
        Assert.Equal("result-draw", result.ResultClass);
    }

    [Fact]
    public void GetResult_InProgressGame_ReturnsNoResult()
    {
        var tree = new GameTree();
        tree.Headers["Result"] = "*";
        tree.AddMoveSan("e4");

        var result = GameResultHelper.GetResult(tree);
        Assert.False(result.HasResult);
    }

    [Fact]
    public void GetResult_CheckmatePosition_AutoDetectsAndUpdatesHeaders()
    {
        // Scholar's Mate: 1. e4 e5 2. Bc4 Nc6 3. Qh5 Nf6 4. Qxf7#
        var tree = new GameTree();
        tree.AddMoveSan("e4");
        tree.AddMoveSan("e5");
        tree.AddMoveSan("Bc4");
        tree.AddMoveSan("Nc6");
        tree.AddMoveSan("Qh5");
        tree.AddMoveSan("Nf6");
        tree.AddMoveSan("Qxf7#");

        Assert.Equal("1-0", tree.Headers["Result"]);
        Assert.Equal("Checkmate", tree.Headers["Termination"]);

        var result = GameResultHelper.GetResult(tree);
        Assert.True(result.HasResult);
        Assert.Equal("1-0", result.Score);
        Assert.Equal("White won by checkmate", result.Description);
        Assert.Equal("result-white-win", result.ResultClass);
    }

    [Fact]
    public void GetResult_StalematePosition_AutoDetectsAndUpdatesHeaders()
    {
        // Setup stalemate position: White Ka8, Qc7; Black Ka6 (after move) or 2-piece stalemate:
        // FEN: 7k/5Q2/6K1/8/8/8/8/8 b - - 0 1 (Black king on h8 is stalemated by White Qf7 and Kg6)
        var tree = new GameTree(startFen: "7k/8/6K1/5Q2/8/8/8/8 w - - 0 1");
        tree.AddMoveSan("Qf7");

        Assert.Equal("1/2-1/2", tree.Headers["Result"]);
        Assert.Equal("Stalemate", tree.Headers["Termination"]);

        var result = GameResultHelper.GetResult(tree);
        Assert.True(result.HasResult);
        Assert.Equal("½-½", result.DisplayScore);
        Assert.Equal("Draw by stalemate", result.Description);
        Assert.Equal("result-draw", result.ResultClass);
    }
}
