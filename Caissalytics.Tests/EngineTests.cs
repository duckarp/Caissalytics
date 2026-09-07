using Caissalytics.Engine;
using Xunit;

namespace Caissalytics.Tests;

public class EngineTests
{
    [Fact]
    public async Task InstallAndRun_Stockfish_Works()
    {
        using var manager = new EngineManager();
        var engines = await manager.GetEnginesAsync();
        Assert.NotEmpty(engines);

        var sf = engines.First(e => e.Id == "stockfish-17");
        if (!sf.IsInstalled)
        {
            var progress = new Progress<int>();
            bool installed = await manager.InstallEngineAsync("stockfish-17", progress);
            Assert.True(installed, "Stockfish installation failed");
        }

        var active = await manager.GetActiveEngineAsync();
        Assert.NotNull(active);
        Assert.True(active.IsInstalled);
        Assert.True(File.Exists(active.ExecutablePath));

        // Test UCI Analysis
        var tcs = new TaskCompletionSource<List<EngineEvaluationLine>>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await manager.StartAnalysisAsync(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            multiPv: 2,
            lines =>
            {
                if (lines.Count >= 2 && lines.All(l => l.Depth >= 4))
                {
                    tcs.TrySetResult(lines);
                }
            },
            cts.Token);

        var resultLines = await tcs.Task;
        await manager.StopAnalysisAsync();

        Assert.NotNull(resultLines);
        Assert.True(resultLines.Count >= 2);
        Assert.True(resultLines[0].Depth >= 4);
        Assert.NotEmpty(resultLines[0].PvMoves);
        Assert.NotNull(resultLines[0].Centipawns);
    }
}
