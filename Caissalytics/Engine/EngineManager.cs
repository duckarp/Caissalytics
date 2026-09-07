using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Caissalytics.Core;

namespace Caissalytics.Engine;

public class EngineManager : IEngineService, IDisposable
{
    private readonly string _engineStorageDir;
    private readonly List<EngineInfo> _engines = new();
    private string _activeEngineId = "stockfish-17";
    private UciProcessClient? _activeClient;
    private readonly HttpClient _httpClient = new();
    private readonly object _lock = new();

    public bool IsAnalyzing => _activeClient != null && _activeClient.IsRunning;

    public EngineManager()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _engineStorageDir = Path.Combine(localAppData, "Caissalytics", "engines");
        Directory.CreateDirectory(_engineStorageDir);

        InitEngines();
        ScanForInstalledEngines();
    }

    private void InitEngines()
    {
        _engines.Add(new EngineInfo
        {
            Id = "stockfish-17",
            Name = "Stockfish 17",
            Version = "17.0",
            Author = "The Stockfish Developers",
            DownloadUrl = "https://github.com/official-stockfish/Stockfish/releases/download/sf_17/stockfish-ubuntu-x86-64-avx2.tar"
        });
    }

    private void ScanForInstalledEngines()
    {
        foreach (var eng in _engines)
        {
            string engineDir = Path.Combine(_engineStorageDir, eng.Id);
            if (Directory.Exists(engineDir))
            {
                var files = Directory.GetFiles(engineDir, "*", SearchOption.AllDirectories);
                var binary = files.FirstOrDefault(f => !f.EndsWith(".tar") && !f.EndsWith(".txt") && !f.EndsWith(".md"));
                if (binary != null)
                {
                    eng.ExecutablePath = binary;
                    eng.IsInstalled = true;
                }
            }

            // Also check system PATH
            if (!eng.IsInstalled && eng.Id.StartsWith("stockfish"))
            {
                string? systemSf = FindInPath("stockfish");
                if (systemSf != null)
                {
                    eng.ExecutablePath = systemSf;
                    eng.IsInstalled = true;
                }
            }
        }
    }

    private static string? FindInPath(string binaryName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        var paths = pathEnv.Split(Path.PathSeparator);
        foreach (var p in paths)
        {
            string full = Path.Combine(p, binaryName);
            if (File.Exists(full))
                return full;
        }
        return null;
    }

    public Task<IReadOnlyList<EngineInfo>> GetEnginesAsync()
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<EngineInfo>>(_engines.ToList());
        }
    }

    public Task<EngineInfo?> GetActiveEngineAsync()
    {
        lock (_lock)
        {
            var eng = _engines.FirstOrDefault(e => e.Id == _activeEngineId) ?? _engines.FirstOrDefault(e => e.IsInstalled);
            return Task.FromResult(eng);
        }
    }

    public Task SetActiveEngineAsync(string engineId)
    {
        lock (_lock)
        {
            _activeEngineId = engineId;
        }
        return Task.CompletedTask;
    }

    public async Task<bool> InstallEngineAsync(string engineId, IProgress<int>? progress = null)
    {
        var engine = _engines.FirstOrDefault(e => e.Id == engineId);
        if (engine == null || string.IsNullOrEmpty(engine.DownloadUrl))
            return false;

        string targetDir = Path.Combine(_engineStorageDir, engine.Id);
        Directory.CreateDirectory(targetDir);
        string tempTar = Path.Combine(targetDir, "engine_download.tar");

        try
        {
            progress?.Report(10);

            using (var response = await _httpClient.GetAsync(engine.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using var fs = new FileStream(tempTar, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);
            }

            progress?.Report(60);

            // Extract tar archive
            TarFile.ExtractToDirectory(tempTar, targetDir, overwriteFiles: true);

            try { File.Delete(tempTar); } catch { }

            progress?.Report(85);

            // Find executable
            var files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
            var binary = files.FirstOrDefault(f => !f.EndsWith(".tar") && !f.EndsWith(".txt") && !f.EndsWith(".md"));

            if (binary == null)
                return false;

            // Mark executable on Unix/Linux
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(binary,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            engine.ExecutablePath = binary;
            engine.IsInstalled = true;
            _activeEngineId = engine.Id;

            progress?.Report(100);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EngineManager] Failed to install engine {engineId}: {ex.Message}");
            return false;
        }
    }

    public Task SetCustomEnginePathAsync(string name, string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Engine executable not found at path.", path);

        lock (_lock)
        {
            string id = $"custom-{Guid.NewGuid():N}";
            var eng = new EngineInfo
            {
                Id = id,
                Name = name,
                ExecutablePath = path,
                IsInstalled = true,
                Version = "Custom"
            };
            _engines.Add(eng);
            _activeEngineId = id;
        }
        return Task.CompletedTask;
    }

    public async Task StartAnalysisAsync(string fen, int multiPv, Action<List<EngineEvaluationLine>> onUpdate, CancellationToken ct = default)
    {
        var engine = await GetActiveEngineAsync();
        if (engine == null || !engine.IsInstalled || string.IsNullOrEmpty(engine.ExecutablePath))
            return;

        if (_activeClient == null || !_activeClient.IsRunning)
        {
            _activeClient?.Dispose();
            _activeClient = new UciProcessClient();
            bool started = await _activeClient.StartEngineAsync(engine.ExecutablePath);
            if (!started) return;
        }

        await _activeClient.StartAnalysisAsync(fen, multiPv, onUpdate);
    }

    public async Task StopAnalysisAsync()
    {
        if (_activeClient != null)
        {
            await _activeClient.StopAnalysisAsync();
        }
    }

    public void Dispose()
    {
        _activeClient?.Dispose();
        _activeClient = null;
        _httpClient.Dispose();
    }
}
