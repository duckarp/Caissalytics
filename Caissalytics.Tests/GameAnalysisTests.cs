using System.Globalization;
using Caissalytics.Components;
using Caissalytics.Components.Analysis;
using Caissalytics.Core;
using Caissalytics.Engine;
using Xunit;

namespace Caissalytics.Tests;

public class FakeEngineService : IEngineService
{
    public Task<IReadOnlyList<EngineInfo>> GetEnginesAsync() => Task.FromResult<IReadOnlyList<EngineInfo>>([]);
    public Task<EngineInfo?> GetActiveEngineAsync() => Task.FromResult<EngineInfo?>(null);
    public Task SetActiveEngineAsync(string engineId) => Task.CompletedTask;
    public Task<bool> InstallEngineAsync(string engineId, IProgress<int>? progress = null) => Task.FromResult(true);
    public Task<EngineProbeResult> ProbeEngineFileAsync(string executablePath) => Task.FromResult(new EngineProbeResult { Success = true, Name = "FakeEngine" });
    public Task<bool> AddCustomEngineAsync(string name, string executablePath) => Task.FromResult(true);
    public Task<bool> RemoveEngineAsync(string engineId) => Task.FromResult(true);
    public Task<IReadOnlyList<EngineInfo>> ScanSystemEnginesAsync() => Task.FromResult<IReadOnlyList<EngineInfo>>([]);
    public Task SetCustomEnginePathAsync(string name, string path) => Task.CompletedTask;
    public Task StartAnalysisAsync(string fen, int multiPv, Action<List<EngineEvaluationLine>> onUpdate, CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAnalysisAsync() => Task.CompletedTask;
    public bool IsAnalyzing => false;
    public string? SyzygyPath { get; set; }
    public Task SetSyzygyPathAsync(string? path) { SyzygyPath = path; return Task.CompletedTask; }
    public Task<StockfishUpdateInfo> CheckStockfishUpdateAsync(bool force = false, CancellationToken ct = default) => Task.FromResult(new StockfishUpdateInfo());
    public Task<bool> UpdateStockfishAsync(IProgress<int>? progress = null, CancellationToken ct = default) => Task.FromResult(true);
    public StockfishUpdateInfo? CachedStockfishUpdate => null;
    public event Action<StockfishUpdateInfo>? OnStockfishUpdateChanged { add { } remove { } }
    public event Action? OnEnginesChanged { add { } remove { } }
}

public class GameAnalysisTests
{
    [Fact]
    public void WinRate_Conversion_MatchesStandardCurve()
    {
        var zeroLine = new EngineEvaluationLine { Centipawns = 0 };
        Assert.Equal(50.0, Math.Round(zeroLine.WhiteWinPercentage, 1));

        var plus300Line = new EngineEvaluationLine { Centipawns = 300 };
        Assert.InRange(plus300Line.WhiteWinPercentage, 74.0, 76.0);

        var minus300Line = new EngineEvaluationLine { Centipawns = -300 };
        Assert.InRange(minus300Line.WhiteWinPercentage, 24.0, 26.0);

        var mateLine = new EngineEvaluationLine { MateInMoves = 2 };
        Assert.Equal(100.0, mateLine.WhiteWinPercentage);

        var losingMateLine = new EngineEvaluationLine { MateInMoves = -1 };
        Assert.Equal(0.0, losingMateLine.WhiteWinPercentage);
    }

    [Fact]
    public void ParseUciMove_HandlesStandardAndPromotions()
    {
        var pos = FenParser.Parse(BoardPosition.StartFen);

        var e4 = GameAnalysisService.ParseUciMove("e2e4", pos);
        Assert.False(e4.IsEmpty);
        Assert.Equal(Square.E2, e4.From);
        Assert.Equal(Square.E4, e4.To);

        // Promotion test position
        var promoPos = FenParser.Parse("8/4P3/8/8/8/8/8/4K2k w - - 0 1");
        var promoQueen = GameAnalysisService.ParseUciMove("e7e8q", promoPos);
        Assert.False(promoQueen.IsEmpty);
        Assert.Equal(PieceType.Queen, promoQueen.Promotion);

        var promoKnight = GameAnalysisService.ParseUciMove("e7e8n", promoPos);
        Assert.False(promoKnight.IsEmpty);
        Assert.Equal(PieceType.Knight, promoKnight.Promotion);
    }

    [Fact]
    public void AnnotateGameTree_InsertsNagsAndRefutations()
    {
        var service = new GameAnalysisService(new FakeEngineService());

        // Scholar's Mate: 1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 4. Qxf7#
        var tree = new GameTree();
        tree.AddMoveSan("e4");
        tree.AddMoveSan("e5");
        tree.AddMoveSan("Qh5");
        tree.AddMoveSan("Nc6");
        tree.AddMoveSan("Bc4");
        var nf6Node = tree.AddMoveSan("Nf6"); // Ply 5: Blunder (allows Qxf7#)
        Assert.NotNull(nf6Node);
        tree.AddMoveSan("Qxf7#");

        var report = new GameAnalysisReport();
        report.Plies.Add(new PlyAnalysis
        {
            Ply = 5,
            MoveSan = "Nf6",
            Move = nf6Node.Move,
            Classification = MoveClassification.Blunder,
            NagNumber = 4,
            NagGlyph = "??",
            FormattedScoreAfter = "+M1",
            BestLineMoves = new List<string> { "g6", "Qf3", "Nf6" }
        });

        service.AnnotateGameTree(tree, report);

        // Verify Nf6 node was annotated with ?? and comment
        Assert.Contains(4, nf6Node.Nags);
        Assert.Contains("[+M1]", nf6Node.Comment);

        // Verify refutation variation was added to parent of Nf6 (3. Bc4)
        var parent = nf6Node.Parent;
        Assert.NotNull(parent);
        Assert.True(parent.Children.Count >= 2);

        var refutationChild = parent.Children[1];
        Assert.Equal("g6", refutationChild.San);
        Assert.Contains(1, refutationChild.Nags); // $1 Good move
        Assert.Contains("Best was g6", refutationChild.Comment);

        // Verify subsequent moves in refutation
        Assert.Single(refutationChild.Children);
        Assert.Equal("Qf3", refutationChild.Children[0].San);
    }

    [Fact]
    public void WorkspacePersistence_PersistsGameAnalysisReport()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreateAnalysisTab("Analyzed Game");
        tab.Tree.AddMoveSan("e4");
        tab.Tree.AddMoveSan("e5");

        var report = new GameAnalysisReport
        {
            WhiteAccuracy = 94.5,
            BlackAccuracy = 82.3,
            WhiteAcpl = 18.2,
            BlackAcpl = 35.7,
            WhiteBestCount = 1,
            BlackMistakeCount = 1
        };
        report.Plies.Add(new PlyAnalysis
        {
            Ply = 0,
            MoveSan = "e4",
            Classification = MoveClassification.Best,
            FormattedScoreAfter = "+0.25"
        });
        report.Plies.Add(new PlyAnalysis
        {
            Ply = 1,
            MoveSan = "e5",
            Classification = MoveClassification.Mistake,
            FormattedScoreAfter = "+1.40"
        });

        tab.AnalysisReport = report;

        string json = ws.ExportStateJson();

        var ws2 = new WorkspaceState();
        bool restored = ws2.RestoreStateFromJson(json);

        Assert.True(restored);
        var restoredTab = ws2.Tabs.OfType<AnalysisTab>().FirstOrDefault();
        Assert.NotNull(restoredTab);
        Assert.NotNull(restoredTab.AnalysisReport);

        var r = restoredTab.AnalysisReport;
        Assert.Equal(94.5, r.WhiteAccuracy);
        Assert.Equal(82.3, r.BlackAccuracy);
        Assert.Equal(18.2, r.WhiteAcpl);
        Assert.Equal(2, r.Plies.Count);
        Assert.Equal("e4", r.Plies[0].MoveSan);
        Assert.Equal(MoveClassification.Best, r.Plies[0].Classification);
        Assert.Equal("e5", r.Plies[1].MoveSan);
        Assert.Equal(MoveClassification.Mistake, r.Plies[1].Classification);
    }

    [Fact]
    public void WorkspacePersistence_HandlesLargeGameReportExceeding32Kb()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreateAnalysisTab("Deep Analyzed Game");

        var report = new GameAnalysisReport
        {
            WhiteAccuracy = 88.0,
            BlackAccuracy = 76.5,
            WhiteAcpl = 25.0,
            BlackAcpl = 45.0
        };

        for (int i = 0; i < 100; i++)
        {
            report.Plies.Add(new PlyAnalysis
            {
                Ply = i,
                MoveSan = i % 2 == 0 ? "Nf3" : "Nf6",
                Classification = i % 5 == 0 ? MoveClassification.Mistake : MoveClassification.Best,
                FormattedScoreAfter = "+0.45",
                FenBefore = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                FenAfter = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
                BestLineMoves = new List<string> { "d4", "d5", "c4", "c6", "Nc3" }
            });
        }

        tab.AnalysisReport = report;
        string json = ws.ExportStateJson();

        // Ensure the JSON payload comfortably exceeds 32KB (default SignalR limit)
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) > 32768);

        var ws2 = new WorkspaceState();
        bool restored = ws2.RestoreStateFromJson(json);

        Assert.True(restored);
        var restoredTab = ws2.Tabs.OfType<AnalysisTab>().FirstOrDefault();
        Assert.NotNull(restoredTab?.AnalysisReport);
        Assert.Equal(100, restoredTab.AnalysisReport.Plies.Count);
    }

    [Fact]
    public void PromoteVariation_PromotesAllAncestorsToRootMainline()
    {
        var tree = new GameTree();
        var e4 = tree.AddMoveSan("e4");
        var e5 = tree.AddMoveSan("e5");
        var nf3 = tree.AddMoveSan("Nf3");

        // Navigate back to e4 and add a variation (e4 c5 Nf3 d6 d4)
        tree.NavigateTo(e4!);
        var c5 = tree.AddMoveSan("c5");
        var nf3Var = tree.AddMoveSan("Nf3");
        var d6 = tree.AddMoveSan("d6");

        // Currently, e5 is mainline (idx 0 of e4.Children), c5 is variation (idx 1)
        Assert.True(e5!.IsMainline);
        Assert.False(c5!.IsMainline);
        Assert.False(d6!.IsMainline);
        Assert.Equal(3, tree.GetMainlineDepth()); // e4, e5, Nf3

        // Promote d6 (deep in the variation)
        tree.PromoteVariation(d6!);

        // Now c5 -> Nf3 -> d6 is the mainline!
        Assert.True(c5.IsMainline);
        Assert.True(nf3Var!.IsMainline);
        Assert.True(d6.IsMainline);
        Assert.False(e5.IsMainline);
        Assert.False(nf3!.IsMainline);
        Assert.Equal(4, tree.GetMainlineDepth()); // e4, c5, Nf3, d6
    }

    [Fact]
    public void MoveNode_IsMainline_IsFalseForSubVariationsEvenIfFirstChild()
    {
        var tree = new GameTree();
        var e4 = tree.AddMoveSan("e4");
        tree.NavigateTo(tree.Root);
        var d4 = tree.AddMoveSan("d4"); // variation off Root
        var d5 = tree.AddMoveSan("d5"); // first child of d4

        Assert.True(e4!.IsMainline);
        Assert.False(d4!.IsMainline);
        Assert.False(d5!.IsMainline); // d5 is first child of d4, but d4 is a variation!
    }

    [Fact]
    public void FindDeepestLeaf_IdentifiesDeepestBranchWhenMainlineIsTruncated()
    {
        var tree = new GameTree();
        // Truncated mainline: 2 plies
        tree.AddMoveSan("e4");
        var e5 = tree.AddMoveSan("e5");

        // Long variation from takeback: 6 plies
        tree.NavigateTo(tree.Root);
        tree.AddMoveSan("d4");
        tree.AddMoveSan("d5");
        tree.AddMoveSan("c4");
        tree.AddMoveSan("e6");
        tree.AddMoveSan("Nc3");
        var nf6 = tree.AddMoveSan("Nf6");

        Assert.Equal(2, tree.GetMainlineDepth());
        var deepest = tree.FindDeepestLeaf();
        Assert.NotNull(deepest);
        Assert.Equal(nf6, deepest);
        Assert.Equal(6, GameTree.GetDepth(deepest));
        Assert.False(deepest.IsMainline);

        // Promoting deepest branch makes it the mainline
        tree.PromoteVariation(deepest);
        Assert.Equal(6, tree.GetMainlineDepth());
        Assert.True(deepest.IsMainline);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("sk-SK")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("cs-CZ")]
    public void EvaluationChart_BuildCurvePath_FormatsInvariantlyUnderCommaCultures(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            var plies = new List<PlyAnalysis>
            {
                new() { Ply = 0, WinRateAfter = 50.0 },
                new() { Ply = 1, WinRateAfter = 54.3 },
                new() { Ply = 2, WinRateAfter = 48.7 },
                new() { Ply = 3, WinRateAfter = 62.1 }
            };

            string curvePath = EvaluationChart.BuildCurvePath(plies);
            string areaPath = EvaluationChart.BuildAreaPath(plies);

            // In SVG path 'd', numbers must use dot as decimal separator and never comma
            Assert.NotEmpty(curvePath);
            Assert.NotEmpty(areaPath);
            Assert.DoesNotContain(",", curvePath);
            Assert.DoesNotContain(",", areaPath);
            Assert.StartsWith("M 42.0 80.0", curvePath);
            Assert.Contains(" L ", curvePath);
            Assert.StartsWith("M 42.0 80.0", areaPath);
            Assert.EndsWith(" Z", areaPath);

            // Ensure F helper also formats with invariant culture
            Assert.Equal("42.3", EvaluationChart.F(42.34));
            Assert.Equal("18.0", EvaluationChart.F(EvaluationChart.TopY));
            Assert.Equal("142.0", EvaluationChart.F(EvaluationChart.BottomY));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
