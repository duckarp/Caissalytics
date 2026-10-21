using Caissalytics.Core;
using Caissalytics.Engine;
using Xunit;

namespace Caissalytics.Tests;

public class UciProcessClientTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string AfterE4Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

    [Fact]
    public async Task CancelledSearch_LateBestmove_DoesNotAnswerTheNextSearch()
    {
        if (!FakeUciEngine.IsSupported) return;

        // White-to-move searches only finish when stopped, and answer slowly; others answer at once
        using var engine = new FakeUciEngine("""
                go*)
                  case "$POS" in
                    *" w "*) PENDING="e2e4" ;;
                    *) echo "info depth 1 score cp 10 pv e7e5"; echo "bestmove e7e5" ;;
                  esac ;;
                stop)
                  if [ -n "$PENDING" ]; then sleep 0.3; echo "info depth 9 score cp 999 pv $PENDING"; echo "bestmove $PENDING"; PENDING=""; fi ;;
            """);
        using var client = new UciProcessClient();
        Assert.True(await client.StartEngineAsync(engine.ExecutablePath));

        using var cts = new CancellationTokenSource();
        var first = client.SearchPositionAsync(StartFen, movetimeMs: 10_000, maxDepth: 0, multiPv: 1, ct: cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        var (bestMove, lines) = await client.SearchPositionAsync(AfterE4Fen, movetimeMs: 100, maxDepth: 0, multiPv: 1)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("e7e5", bestMove);
        var line = Assert.Single(lines);
        Assert.Equal("e7e5", line.PvMoves[0]);
        Assert.Equal(-10, line.Centipawns); // +10 for Black, reported from White's side
    }

    [Fact]
    public async Task EngineExitingMidSearch_CompletesInsteadOfHanging()
    {
        if (!FakeUciEngine.IsSupported) return;

        using var engine = new FakeUciEngine("""
                go*) exit 1 ;;
            """);
        using var client = new UciProcessClient();
        Assert.True(await client.StartEngineAsync(engine.ExecutablePath));

        var result = await client.EvaluatePositionAsync(StartFen, movetimeMs: 100, maxDepth: 0)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(result);
    }

    [Fact]
    public async Task InfoParsing_IgnoresBoundScores_AndReadsDepthNotSeldepth()
    {
        if (!FakeUciEngine.IsSupported) return;

        using var engine = new FakeUciEngine("""
                go*)
                  echo "info depth 7 seldepth 12 multipv 1 score cp 80 lowerbound nodes 100 pv e2e4"
                  echo "info depth 7 seldepth 12 multipv 1 score cp 30 nodes 200 pv d2d4 d7d5"
                  echo "bestmove d2d4" ;;
            """);
        using var client = new UciProcessClient();
        Assert.True(await client.StartEngineAsync(engine.ExecutablePath));

        var result = await client.EvaluatePositionAsync(StartFen, movetimeMs: 100, maxDepth: 0)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(result);
        Assert.Equal(7, result.Depth);
        Assert.Equal(12, result.SelectiveDepth);
        Assert.Equal(30, result.Centipawns);
        Assert.Equal("d2d4", result.PvMoves[0]);
    }

    [Fact]
    public async Task ConcurrentRequests_SupersededCallerCompletesInsteadOfHanging()
    {
        if (!FakeUciEngine.IsSupported) return;

        // Searches only finish when stopped
        using var engine = new FakeUciEngine("""
                go*) PENDING="e2e4" ;;
                stop) if [ -n "$PENDING" ]; then echo "bestmove $PENDING"; PENDING=""; fi ;;
            """);
        using var client = new UciProcessClient();
        Assert.True(await client.StartEngineAsync(engine.ExecutablePath));

        var first = client.EvaluatePositionAsync(StartFen, movetimeMs: 10_000, maxDepth: 0);
        await Task.Delay(100);
        using var cts = new CancellationTokenSource();
        var second = client.EvaluatePositionAsync(StartFen, movetimeMs: 10_000, maxDepth: 0, cts.Token);

        Assert.Null(await first.WaitAsync(TimeSpan.FromSeconds(10)));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}

public class PracticeMoveSelectionTests
{
    private static EngineEvaluationLine Line(int index, string move, double? cp = null, int? mate = null)
        => new() { MultiPvIndex = index, PvMoves = [move], Centipawns = cp, MateInMoves = mate };

    [Fact]
    public void BotPlayingBlack_AlwaysTakesForcedMate()
    {
        // Scores are from White's side: mate -2 means Black mates in 2
        var lines = new List<EngineEvaluationLine>
        {
            Line(1, "d8h4", mate: -2),
            Line(2, "e7e5", cp: -20),
            Line(3, "g8f6", cp: -10)
        };

        for (int i = 0; i < 300; i++)
        {
            Assert.Equal("d8h4", PracticeEngineService.SelectMoveFromLines(lines, "d8h4", elo: 1000, PieceColor.Black));
        }
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1600)]
    [InlineData(2000)]
    public void Bot_NeverPicksAnAlternativeThatGetsItMated(int elo)
    {
        // Bot is White; the second line lets Black mate in 3
        var lines = new List<EngineEvaluationLine>
        {
            Line(1, "e2e4", cp: 0),
            Line(2, "f2f3", mate: -3),
            Line(3, "g2g4", mate: -2)
        };

        for (int i = 0; i < 300; i++)
        {
            Assert.Equal("e2e4", PracticeEngineService.SelectMoveFromLines(lines, "e2e4", elo, PieceColor.White));
        }
    }
}
