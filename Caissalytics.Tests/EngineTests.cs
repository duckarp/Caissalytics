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

    [Theory]
    [InlineData("sf_19", 19.0)]
    [InlineData("Stockfish 19", 19.0)]
    [InlineData("Stockfish 17.0", 17.0)]
    [InlineData("sf_16.1", 16.1)]
    [InlineData("v18.0", 18.0)]
    [InlineData("stockfish-19", 19.0)]
    [InlineData("", 0.0)]
    [InlineData(null, 0.0)]
    public void ParseStockfishVersion_ExtractsExpectedNumber(string? input, double expected)
    {
        double actual = StockfishVersionHelper.ParseStockfishVersion(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SelectBestAsset_SelectsAppropriatePlatformAsset()
    {
        var sampleAssets = new List<StockfishReleaseAsset>
        {
            new("stockfish-android-arm64-universal.tar.gz", "https://example.com/android", 100),
            new("stockfish-linux-x86-64-universal.tar.gz", "https://example.com/linux-x64", 200),
            new("stockfish-linux-arm64-universal.tar.gz", "https://example.com/linux-arm64", 300),
            new("stockfish-macos-universal.tar.gz", "https://example.com/mac-univ", 400),
            new("stockfish-windows-x86-64-universal.zip", "https://example.com/win-x64", 500),
            new("stockfish-windows-arm64-universal.zip", "https://example.com/win-arm64", 600)
        };

        // Linux x64
        var linAsset = StockfishVersionHelper.SelectBestAsset(sampleAssets, System.Runtime.InteropServices.OSPlatform.Linux, System.Runtime.InteropServices.Architecture.X64);
        Assert.NotNull(linAsset);
        Assert.Equal("stockfish-linux-x86-64-universal.tar.gz", linAsset.Name);

        // Windows x64
        var winAsset = StockfishVersionHelper.SelectBestAsset(sampleAssets, System.Runtime.InteropServices.OSPlatform.Windows, System.Runtime.InteropServices.Architecture.X64);
        Assert.NotNull(winAsset);
        Assert.Equal("stockfish-windows-x86-64-universal.zip", winAsset.Name);

        // macOS
        var macAsset = StockfishVersionHelper.SelectBestAsset(sampleAssets, System.Runtime.InteropServices.OSPlatform.OSX, System.Runtime.InteropServices.Architecture.Arm64);
        Assert.NotNull(macAsset);
        Assert.Equal("stockfish-macos-universal.tar.gz", macAsset.Name);
    }

    [Fact]
    public async Task CheckStockfishUpdateAsync_DetectsAvailableUpdate()
    {
        string fakeJson = """
        {
          "tag_name": "sf_19",
          "name": "Stockfish 19",
          "body": "Announcement of Stockfish 19 with universal binaries and SFNNv16 net.",
          "assets": [
            {
              "name": "stockfish-linux-x86-64-universal.tar.gz",
              "size": 81388977,
              "browser_download_url": "https://github.com/official-stockfish/Stockfish/releases/download/sf_19/stockfish-linux-x86-64-universal.tar.gz"
            },
            {
              "name": "stockfish-windows-x86-64-universal.zip",
              "size": 81431614,
              "browser_download_url": "https://github.com/official-stockfish/Stockfish/releases/download/sf_19/stockfish-windows-x86-64-universal.zip"
            }
          ]
        }
        """;

        var mockHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(fakeJson, System.Text.Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(mockHandler);
        string tempDir = Path.Combine(Path.GetTempPath(), $"caissa_update_test_{Guid.NewGuid():N}");
        try
        {
            // Seed tempDir with an existing stockfish-17 marked as installed
            Directory.CreateDirectory(tempDir);
            string configPath = Path.Combine(tempDir, "engines_config.json");
            var config = new EnginesConfigFile
            {
                ActiveEngineId = "stockfish-17",
                Engines = new List<EngineInfo>
                {
                    new()
                    {
                        Id = "stockfish-17",
                        Name = "Stockfish 17",
                        Version = "17.0",
                        IsInstalled = true,
                        ExecutablePath = "/fake/stockfish"
                    }
                }
            };
            await File.WriteAllTextAsync(configPath, System.Text.Json.JsonSerializer.Serialize(config));

            using var manager = new EngineManager(tempDir, httpClient);

            var updateInfo = await manager.CheckStockfishUpdateAsync(force: true);

            Assert.NotNull(updateInfo);
            Assert.True(updateInfo.IsUpdateAvailable);
            Assert.Equal("Stockfish 19", updateInfo.LatestVersion);
            Assert.Equal("sf_19", updateInfo.ReleaseTag);
            Assert.NotEmpty(updateInfo.DownloadUrl);
            Assert.NotEmpty(updateInfo.AssetName);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task UpdateStockfishAsync_ExtractsAndRegistersStockfish()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"caissa_sfupdate_{Guid.NewGuid():N}");
        string zipDir = Path.Combine(Path.GetTempPath(), $"caissa_zip_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(zipDir);

            // Create a fake Stockfish binary/script in zipDir
            string scriptPath = Path.Combine(zipDir, "stockfish-fake");
            string scriptContent = "#!/bin/sh\nwhile read line; do\n  if [ \"$line\" = \"uci\" ]; then\n    echo \"id name Stockfish 19.0\"\n    echo \"id author The Stockfish Developers\"\n    echo \"uciok\"\n  elif [ \"$line\" = \"quit\" ]; then\n    exit 0\n  fi\ndone\n";
            await File.WriteAllTextAsync(scriptPath, scriptContent);
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            string zipPath = Path.Combine(Path.GetTempPath(), $"stockfish_{Guid.NewGuid():N}.zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(zipDir, zipPath);
            byte[] zipBytes = await File.ReadAllBytesAsync(zipPath);
            try { File.Delete(zipPath); } catch { }

            string releaseJson = $$"""
            {
              "tag_name": "sf_19",
              "name": "Stockfish 19",
              "body": "Universal binary release",
              "assets": [
                {
                  "name": "stockfish-linux-x86-64-universal.zip",
                  "size": {{zipBytes.Length}},
                  "browser_download_url": "https://example.com/download/sf.zip"
                },
                {
                  "name": "stockfish-windows-x86-64-universal.zip",
                  "size": {{zipBytes.Length}},
                  "browser_download_url": "https://example.com/download/sf.zip"
                }
              ]
            }
            """;

            var mockHandler = new TestHttpMessageHandler(req =>
            {
                if (req.RequestUri?.ToString().Contains("releases/latest") == true)
                {
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, System.Text.Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(zipBytes)
                };
            });

            using var httpClient = new HttpClient(mockHandler);
            using var manager = new EngineManager(tempDir, httpClient);

            var checkResult = await manager.CheckStockfishUpdateAsync(force: true);
            Assert.NotNull(checkResult);

            var progress = new Progress<int>();
            bool updated = await manager.UpdateStockfishAsync(progress);
            Assert.True(updated);

            var active = await manager.GetActiveEngineAsync();
            Assert.NotNull(active);
            Assert.Equal("stockfish-19", active.Id);
            Assert.Contains("Stockfish 19", active.Name);
            Assert.True(active.IsInstalled);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (Directory.Exists(zipDir)) Directory.Delete(zipDir, true);
        }
    }

    [Fact]
    public async Task RemoveEngineAsync_RemovesNonCustomEngine_AndCleansDisk()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"caissa_engine_remove_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var manager = new EngineManager(tempDir);

            // Simulate an updated Stockfish engine in managed directory
            string sf19Dir = Path.Combine(tempDir, "stockfish-19");
            Directory.CreateDirectory(sf19Dir);
            string binaryPath = Path.Combine(sf19Dir, "stockfish");
            File.WriteAllText(binaryPath, "mock binary");

            // Register Stockfish 19 as non-custom installed engine
            var enginesField = typeof(EngineManager).GetField("_engines", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var enginesList = enginesField?.GetValue(manager) as List<EngineInfo>;
            Assert.NotNull(enginesList);

            enginesList!.Insert(0, new EngineInfo
            {
                Id = "stockfish-19",
                Name = "Stockfish 19",
                Version = "19.0",
                ExecutablePath = binaryPath,
                IsInstalled = true,
                IsCustom = false
            });

            await manager.SetActiveEngineAsync("stockfish-19");
            var active = await manager.GetActiveEngineAsync();
            Assert.Equal("stockfish-19", active?.Id);

            // Removing stockfish-17 should fail (baseline)
            bool removedBaseline = await manager.RemoveEngineAsync("stockfish-17");
            Assert.False(removedBaseline);

            // Removing stockfish-19 should succeed
            bool removed = await manager.RemoveEngineAsync("stockfish-19");
            Assert.True(removed);

            var enginesAfter = await manager.GetEnginesAsync();
            Assert.DoesNotContain(enginesAfter, e => e.Id == "stockfish-19");

            // Active engine should have failed over to stockfish-17
            var activeAfter = await manager.GetActiveEngineAsync();
            Assert.Equal("stockfish-17", activeAfter?.Id);

            // Managed directory should have been deleted from disk
            Assert.False(Directory.Exists(sf19Dir));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _sender;
        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> sender) => _sender = sender;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_sender(request));
    }
}
