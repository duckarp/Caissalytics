using Caissalytics.Core;
using Xunit;

namespace Caissalytics.Tests;

public class GameMergerTests
{
    private static List<string> Line(MoveNode node)
    {
        var sans = new List<string>();
        while (node.Children.Count > 0)
        {
            node = node.Children[0];
            sans.Add(node.San);
        }
        return sans;
    }

    private static MoveNode Walk(GameTree tree, params string[] sans)
    {
        var node = tree.Root;
        foreach (var san in sans) node = node.Children.Single(c => c.San == san);
        return node;
    }

    [Fact]
    public void Divergence_BecomesVariation_AndTargetMainlineStays()
    {
        var target = PgnHandler.ImportPgn("1. e4 e5 2. Nf3 Nc6 3. Bb5 *");
        var source = PgnHandler.ImportPgn("1. e4 e5 2. Nf3 Nf6 3. Nxe5 d6 *");

        var result = GameMerger.Merge(target, source);

        Assert.False(result.StartPositionMismatch);
        Assert.Equal(3, result.MovesAdded);
        Assert.Equal(["e4", "e5", "Nf3", "Nc6", "Bb5"], Line(target.Root));
        var afterNf3 = Walk(target, "e4", "e5", "Nf3");
        Assert.Equal(["Nc6", "Nf6"], afterNf3.Children.Select(c => c.San));
        Assert.Equal(["Nxe5", "d6"], Line(afterNf3.Children[1]));
        Assert.Same(afterNf3, afterNf3.Children[1].Parent);
    }

    [Fact]
    public void SourceLongerThanTarget_ExtendsTheMainline()
    {
        var target = PgnHandler.ImportPgn("1. d4 d5 *");
        var source = PgnHandler.ImportPgn("1. d4 d5 2. c4 e6 *");

        var result = GameMerger.Merge(target, source);

        Assert.Equal(2, result.MovesAdded);
        Assert.Equal(["d4", "d5", "c4", "e6"], Line(target.Root));
    }

    [Fact]
    public void SameGame_AddsNothing()
    {
        var target = PgnHandler.ImportPgn("1. e4 c5 2. Nf3 *");
        var source = PgnHandler.ImportPgn("1. e4 c5 2. Nf3 *");

        Assert.Equal(0, GameMerger.Merge(target, source, "label").MovesAdded);
        Assert.Null(Walk(target, "e4", "c5", "Nf3").Comment);
    }

    [Fact]
    public void Label_GoesOnTheFirstNewMoveOfTheSourceMainline()
    {
        var target = PgnHandler.ImportPgn("1. e4 e5 *");
        var source = PgnHandler.ImportPgn("1. e4 c5 {Sicilian} 2. Nf3 *");

        GameMerger.Merge(target, source, "Carlsen – Nakamura, Wijk 2023");

        Assert.Equal("Carlsen – Nakamura, Wijk 2023 Sicilian", Walk(target, "e4", "c5").Comment);
        Assert.Null(Walk(target, "e4", "c5", "Nf3").Comment);
    }

    [Fact]
    public void SourceVariations_AreMergedToo_WithoutTheLabel()
    {
        var target = PgnHandler.ImportPgn("1. e4 e5 2. Nf3 *");
        var source = PgnHandler.ImportPgn("1. e4 e5 (1... c5 2. Nf3) 2. Nf3 Nc6 *");

        var result = GameMerger.Merge(target, source, "Game");

        Assert.Equal(3, result.MovesAdded);
        Assert.Equal(["e5", "c5"], Walk(target, "e4").Children.Select(c => c.San));
        Assert.Null(Walk(target, "e4", "c5").Comment);
        Assert.Equal("Game", Walk(target, "e4", "e5", "Nf3", "Nc6").Comment);
    }

    [Fact]
    public void SharedMoves_KeepTargetAnnotations_AndFillMissingOnes()
    {
        var target = PgnHandler.ImportPgn("1. e4 {Best by test} e5 2. Nf3 *");
        var source = PgnHandler.ImportPgn("1. e4 {King's pawn} e5 $1 {Solid} 2. Nf3 *");

        GameMerger.Merge(target, source);

        Assert.Equal("Best by test", Walk(target, "e4").Comment);
        var e5 = Walk(target, "e4", "e5");
        Assert.Equal("Solid", e5.Comment);
        Assert.Equal([1], e5.Nags);
    }

    [Fact]
    public void ClockTimes_AreNotCopiedIntoVariations()
    {
        var target = PgnHandler.ImportPgn("1. e4 e5 *");
        var source = PgnHandler.ImportPgn("1. e4 { [%clk 0:05:00] } c5 { [%clk 0:04:58] } *");

        GameMerger.Merge(target, source);

        Assert.Null(Walk(target, "e4", "c5").Clock);
    }

    [Fact]
    public void DifferentStartPositions_AreNotMerged()
    {
        var target = PgnHandler.ImportPgn("1. e4 e5 *");
        var source = PgnHandler.ImportPgn("[SetUp \"1\"]\n[FEN \"4k3/8/8/8/8/8/4P3/4K3 w - - 0 1\"]\n\n1. e4 *");

        var result = GameMerger.Merge(target, source);

        Assert.True(result.StartPositionMismatch);
        Assert.Equal(["e4", "e5"], Line(target.Root));
    }

    [Fact]
    public void MergedGame_SurvivesPgnRoundTrip()
    {
        var target = PgnHandler.ImportPgn("1. e4 e5 2. Nf3 *");
        var source = PgnHandler.ImportPgn("1. e4 c5 2. Nf3 d6 *");
        GameMerger.Merge(target, source, "Source game");

        var reimported = PgnHandler.ImportPgn(PgnHandler.ExportPgn(target));

        Assert.Equal(["e4", "e5", "Nf3"], Line(reimported.Root));
        Assert.Equal("Source game", Walk(reimported, "e4", "c5").Comment);
        Assert.Equal(["Nf3", "d6"], Line(Walk(reimported, "e4", "c5")));
    }

    [Fact]
    public void BuildLabel_UsesPlayersEventYearAndResult()
    {
        var headers = new Dictionary<string, string>
        {
            ["White"] = "Carlsen, M",
            ["Black"] = "Nakamura, H",
            ["Event"] = "Tata Steel",
            ["Date"] = "2023.01.20",
            ["Result"] = "1-0",
        };

        Assert.Equal("Carlsen, M – Nakamura, H, Tata Steel 2023 (1-0)", GameMerger.BuildLabel(headers));
    }

    [Fact]
    public void BuildLabel_LeavesOutUnknownParts()
    {
        var headers = new Dictionary<string, string>
        {
            ["White"] = "Novak",
            ["Black"] = "?",
            ["Event"] = "?",
            ["Date"] = "????.??.??",
            ["Result"] = "*",
        };

        Assert.Equal("Novak – ?", GameMerger.BuildLabel(headers));
        Assert.Equal("", GameMerger.BuildLabel(new Dictionary<string, string>()));
    }
    [Fact]
    public void GameCount_CountsGamesThatPlayedEachMove()
    {
        var target = PgnHandler.ImportPgn("1. e4 e5 2. Nf3 *");
        GameMerger.Merge(target, PgnHandler.ImportPgn("1. e4 c5 2. Nf3 *"));
        GameMerger.Merge(target, PgnHandler.ImportPgn("1. e4 c5 2. Nc3 *"));

        Assert.Equal(3, Walk(target, "e4").GameCount);
        Assert.Equal(1, Walk(target, "e4", "e5").GameCount);
        Assert.Equal(2, Walk(target, "e4", "c5").GameCount);
        Assert.Equal(1, Walk(target, "e4", "c5", "Nf3").GameCount);
        Assert.Equal(1, Walk(target, "e4", "c5", "Nc3").GameCount);
    }

    [Fact]
    public void GameCount_IgnoresSourceSideLines()
    {
        var target = PgnHandler.ImportPgn("1. e4 e5 *");
        GameMerger.Merge(target, PgnHandler.ImportPgn("1. e4 e5 (1... c5) *"));

        Assert.Equal(2, Walk(target, "e4", "e5").GameCount);
        Assert.Equal(0, Walk(target, "e4", "c5").GameCount);
    }

    [Fact]
    public void GameCount_AddsUpWhenMergedGamesAreMergedAgain()
    {
        var first = PgnHandler.ImportPgn("1. d4 d5 *");
        GameMerger.Merge(first, PgnHandler.ImportPgn("1. d4 Nf6 *"));
        var second = PgnHandler.ImportPgn("1. d4 d5 *");
        GameMerger.Merge(second, PgnHandler.ImportPgn("1. d4 d5 *"));

        GameMerger.Merge(first, second);

        Assert.Equal(4, Walk(first, "d4").GameCount);
        Assert.Equal(3, Walk(first, "d4", "d5").GameCount);
        Assert.Equal(1, Walk(first, "d4", "Nf6").GameCount);
    }

    [Fact]
    public void GameCount_SurvivesPgnRoundTrip_NextToClockAndComment()
    {
        var target = PgnHandler.ImportPgn("1. e4 { [%clk 0:05:00] Good } e5 *");
        GameMerger.Merge(target, PgnHandler.ImportPgn("1. e4 e5 *"));

        string pgn = PgnHandler.ExportPgn(target);
        var reimported = PgnHandler.ImportPgn(pgn);

        Assert.Contains("[%games 2]", pgn);
        var e4 = Walk(reimported, "e4");
        Assert.Equal(2, e4.GameCount);
        Assert.Equal("0:05:00", e4.Clock);
        Assert.Equal("Good", e4.Comment);
    }

    [Fact]
    public void SetMergedHeaders_KeepsCommonTags_AndMarksDifferingPlayers()
    {
        var a = PgnHandler.ImportPgn("[Event \"Open\"]\n[White \"Novak\"]\n[Black \"Kral\"]\n[Date \"2024.01.01\"]\n[Result \"1-0\"]\n[ECO \"C20\"]\n\n1. e4 e5 1-0");
        var b = PgnHandler.ImportPgn("[Event \"Open\"]\n[White \"Novak\"]\n[Black \"Horak\"]\n[Date \"2024.02.01\"]\n[Result \"0-1\"]\n[ECO \"B20\"]\n\n1. e4 c5 0-1");

        GameMerger.SetMergedHeaders(a, [a.Headers, b.Headers], "Various");

        Assert.Equal("Novak", a.Headers["White"]);
        Assert.Equal("Various", a.Headers["Black"]);
        Assert.Equal("Open", a.Headers["Event"]);
        Assert.Equal("????.??.??", a.Headers["Date"]);
        Assert.Equal("*", a.Headers["Result"]);
        Assert.False(a.Headers.ContainsKey("ECO"));
    }
}
