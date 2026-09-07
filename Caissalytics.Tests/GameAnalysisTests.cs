using Caissalytics.Components;
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
}
