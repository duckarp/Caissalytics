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

    [Fact]
    public async Task ProbeEngineFile_NonExistentFile_ReturnsFailure()
    {
        using var manager = new EngineManager();
        var result = await manager.ProbeEngineFileAsync("/non/existent/path/to/engine");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ProbeEngineFile_EmptyPath_ReturnsFailure()
    {
        using var manager = new EngineManager();
        var result = await manager.ProbeEngineFileAsync("");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task EngineManager_PersistenceAndActiveSwitching_Works()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"caissa_engine_test_{Guid.NewGuid():N}");
        try
        {
            using (var manager1 = new EngineManager(tempDir))
            {
                var engines = await manager1.GetEnginesAsync();
                Assert.Contains(engines, e => e.Id == "stockfish-17");

                // Check default active
                var active = await manager1.GetActiveEngineAsync();
                Assert.NotNull(active);
                Assert.Equal("stockfish-17", active.Id);
            }

            // Create a fake custom engine config directly to test persistence roundtrip
            string configPath = Path.Combine(tempDir, "engines_config.json");
            var config = new EnginesConfigFile
            {
                ActiveEngineId = "custom-test-1",
                Engines = new List<EngineInfo>
                {
                    new EngineInfo
                    {
                        Id = "stockfish-17",
                        Name = "Stockfish 17",
                        IsInstalled = false
                    },
                    new EngineInfo
                    {
                        Id = "custom-test-1",
                        Name = "My Custom UCI Engine",
                        Author = "Test Author",
                        ExecutablePath = "/fake/custom/engine",
                        IsInstalled = true,
                        IsCustom = true
                    }
                }
            };
            var json = System.Text.Json.JsonSerializer.Serialize(config);
            await File.WriteAllTextAsync(configPath, json);

            // Open with manager2
            using (var manager2 = new EngineManager(tempDir))
            {
                var engines2 = await manager2.GetEnginesAsync();
                Assert.Equal(2, engines2.Count);

                var custom = engines2.FirstOrDefault(e => e.Id == "custom-test-1");
                Assert.NotNull(custom);
                Assert.True(custom.IsCustom);
                Assert.True(custom.IsActive);
                Assert.Equal("My Custom UCI Engine", custom.Name);

                // Switch active back to stockfish-17
                await manager2.SetActiveEngineAsync("stockfish-17");
                var active2 = await manager2.GetActiveEngineAsync();
                Assert.Equal("stockfish-17", active2?.Id);

                // Cannot remove built-in stockfish
                bool removedSf = await manager2.RemoveEngineAsync("stockfish-17");
                Assert.False(removedSf);

                // Can remove custom engine
                bool removedCustom = await manager2.RemoveEngineAsync("custom-test-1");
                Assert.True(removedCustom);

                var enginesAfterRemoval = await manager2.GetEnginesAsync();
                Assert.DoesNotContain(enginesAfterRemoval, e => e.Id == "custom-test-1");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
