using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Caissalytics.Core;

namespace Caissalytics.Engine;

public class EngineManager : IEngineService, IDisposable
{
    private readonly string _engineStorageDir;
    private readonly string _configFilePath;
    private readonly List<EngineInfo> _engines = new();
    private string _activeEngineId = "stockfish-17";
    private UciProcessClient? _activeClient;
    private readonly HttpClient _httpClient = new();
    private readonly object _lock = new();

    public event Action? OnEnginesChanged;

    public bool IsAnalyzing => _activeClient != null && _activeClient.IsRunning;

    public EngineManager()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _engineStorageDir = Path.Combine(localAppData, "Caissalytics", "engines");
        Directory.CreateDirectory(_engineStorageDir);
        _configFilePath = Path.Combine(_engineStorageDir, "engines_config.json");

        LoadConfig();
        ScanForInstalledEngines();
    }

    public EngineManager(string customStorageDir)
    {
        _engineStorageDir = customStorageDir;
        Directory.CreateDirectory(_engineStorageDir);
        _configFilePath = Path.Combine(_engineStorageDir, "engines_config.json");

        LoadConfig();
        ScanForInstalledEngines();
    }

    private void LoadConfig()
    {
        lock (_lock)
        {
            _engines.Clear();

            if (File.Exists(_configFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_configFilePath);
                    var config = JsonSerializer.Deserialize<EnginesConfigFile>(json);
                    if (config != null)
                    {
                        _activeEngineId = config.ActiveEngineId;
                        _engines.AddRange(config.Engines);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EngineManager] Error loading config: {ex.Message}");
                }
            }

            // Ensure official Stockfish 17 is always in the registry
            if (!_engines.Any(e => e.Id == "stockfish-17"))
            {
                _engines.Insert(0, new EngineInfo
                {
                    Id = "stockfish-17",
                    Name = "Stockfish 17",
                    Version = "17.0",
                    Author = "The Stockfish Developers",
                    DownloadUrl = "https://github.com/official-stockfish/Stockfish/releases/download/sf_17/stockfish-ubuntu-x86-64-avx2.tar",
                    IsCustom = false
                });
            }

            UpdateActiveFlag();
        }
    }

    private void SaveConfig()
    {
        lock (_lock)
        {
            try
            {
                var config = new EnginesConfigFile
                {
                    ActiveEngineId = _activeEngineId,
                    Engines = _engines.ToList()
                };
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EngineManager] Error saving config: {ex.Message}");
            }
        }
    }

    private void UpdateActiveFlag()
    {
        foreach (var eng in _engines)
        {
            eng.IsActive = (eng.Id == _activeEngineId);
        }
    }

    private void ScanForInstalledEngines()
    {
        lock (_lock)
        {
            foreach (var eng in _engines)
            {
                // Check if executable already exists at path
                if (!string.IsNullOrEmpty(eng.ExecutablePath) && File.Exists(eng.ExecutablePath))
                {
                    eng.IsInstalled = true;
                    continue;
                }

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

                // Also check system PATH for built-in Stockfish
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

            UpdateActiveFlag();
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
            UpdateActiveFlag();
            return Task.FromResult<IReadOnlyList<EngineInfo>>(_engines.ToList());
        }
    }

    public Task<EngineInfo?> GetActiveEngineAsync()
    {
        lock (_lock)
        {
            var eng = _engines.FirstOrDefault(e => e.Id == _activeEngineId)
                   ?? _engines.FirstOrDefault(e => e.IsInstalled);
            return Task.FromResult(eng);
        }
    }

    public async Task SetActiveEngineAsync(string engineId)
    {
        lock (_lock)
        {
            if (_activeEngineId == engineId) return;
            _activeEngineId = engineId;
            UpdateActiveFlag();
            SaveConfig();
        }

        if (_activeClient != null)
        {
            await _activeClient.StopAnalysisAsync();
            _activeClient.Dispose();
            _activeClient = null;
        }

        OnEnginesChanged?.Invoke();
    }

    public async Task<bool> InstallEngineAsync(string engineId, IProgress<int>? progress = null)
    {
        EngineInfo? engine;
        lock (_lock)
        {
            engine = _engines.FirstOrDefault(e => e.Id == engineId);
        }

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

            TarFile.ExtractToDirectory(tempTar, targetDir, overwriteFiles: true);

            try { File.Delete(tempTar); } catch { }

            progress?.Report(85);

            var files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
            var binary = files.FirstOrDefault(f => !f.EndsWith(".tar") && !f.EndsWith(".txt") && !f.EndsWith(".md"));

            if (binary == null)
                return false;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(binary,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            lock (_lock)
            {
                engine.ExecutablePath = binary;
                engine.IsInstalled = true;
                _activeEngineId = engine.Id;
                UpdateActiveFlag();
                SaveConfig();
            }

            progress?.Report(100);
            OnEnginesChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EngineManager] Failed to install engine {engineId}: {ex.Message}");
            return false;
        }
    }

    public async Task<EngineProbeResult> ProbeEngineFileAsync(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new EngineProbeResult { Success = false, ErrorMessage = "Executable path is empty." };
        }

        if (!File.Exists(executablePath))
        {
            return new EngineProbeResult { Success = false, ErrorMessage = $"File not found at: {executablePath}" };
        }

        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var mode = File.GetUnixFileMode(executablePath);
                    if (!mode.HasFlag(UnixFileMode.UserExecute))
                    {
                        File.SetUnixFileMode(executablePath, mode | UnixFileMode.UserExecute);
                    }
                }
                catch { }
            }

            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            string engineName = "";
            string author = "";
            bool uciOk = false;
            var tcs = new TaskCompletionSource<bool>();

            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                if (e.Data.StartsWith("id name "))
                {
                    engineName = e.Data.Substring("id name ".Length).Trim();
                }
                else if (e.Data.StartsWith("id author "))
                {
                    author = e.Data.Substring("id author ".Length).Trim();
                }
                else if (e.Data.Trim() == "uciok" || e.Data.Trim() == "readyok")
                {
                    uciOk = true;
                    tcs.TrySetResult(true);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.StandardInput.WriteLineAsync("uci");
            await process.StandardInput.FlushAsync();

            var timeoutTask = Task.Delay(3000);
            var completed = await Task.WhenAny(tcs.Task, timeoutTask);

            try
            {
                await process.StandardInput.WriteLineAsync("quit");
                await process.StandardInput.FlushAsync();
                process.WaitForExit(500);
            }
            catch { }

            if (!process.HasExited)
            {
                try { process.Kill(); } catch { }
            }

            if (uciOk || !string.IsNullOrEmpty(engineName))
            {
                return new EngineProbeResult
                {
                    Success = true,
                    Name = !string.IsNullOrEmpty(engineName) ? engineName : Path.GetFileName(executablePath),
                    Author = author
                };
            }

            return new EngineProbeResult
            {
                Success = false,
                ErrorMessage = "The executable did not respond with standard UCI handshake ('uciok')."
            };
        }
        catch (Exception ex)
        {
            return new EngineProbeResult
            {
                Success = false,
                ErrorMessage = $"Failed to execute binary: {ex.Message}"
            };
        }
    }

    public async Task<bool> AddCustomEngineAsync(string name, string executablePath)
    {
        var probe = await ProbeEngineFileAsync(executablePath);
        if (!probe.Success)
        {
            return false;
        }

        string finalName = !string.IsNullOrWhiteSpace(name) ? name.Trim() : probe.Name;
        string id = $"custom-{Guid.NewGuid():N}"[..15];

        lock (_lock)
        {
            var eng = new EngineInfo
            {
                Id = id,
                Name = finalName,
                Author = probe.Author,
                ExecutablePath = executablePath,
                IsInstalled = true,
                IsCustom = true,
                DateAdded = DateTime.UtcNow
            };

            _engines.Add(eng);
            _activeEngineId = id;
            UpdateActiveFlag();
            SaveConfig();
        }

        OnEnginesChanged?.Invoke();
        return true;
    }

    public Task SetCustomEnginePathAsync(string name, string path)
    {
        return AddCustomEngineAsync(name, path);
    }

    public Task<bool> RemoveEngineAsync(string engineId)
    {
        lock (_lock)
        {
            var eng = _engines.FirstOrDefault(e => e.Id == engineId);
            if (eng == null || !eng.IsCustom)
            {
                return Task.FromResult(false);
            }

            _engines.Remove(eng);

            if (_activeEngineId == engineId)
            {
                _activeEngineId = _engines.FirstOrDefault(e => e.IsInstalled)?.Id ?? "stockfish-17";
            }

            UpdateActiveFlag();
            SaveConfig();
        }

        OnEnginesChanged?.Invoke();
        return Task.FromResult(true);
    }

    public async Task<IReadOnlyList<EngineInfo>> ScanSystemEnginesAsync()
    {
        var candidateNames = new[] { "stockfish", "stockfish17", "stockfish16", "lc0", "komodo", "crafty" };
        var candidateDirs = new[] { "/usr/games", "/usr/bin", "/usr/local/bin", "/opt/chess" };
        var foundList = new List<string>();

        // Check candidate directories
        foreach (var dir in candidateDirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var name in candidateNames)
            {
                string path = Path.Combine(dir, name);
                if (File.Exists(path) && !foundList.Contains(path))
                {
                    foundList.Add(path);
                }
            }
        }

        // Also check PATH
        foreach (var name in candidateNames)
        {
            string? inPath = FindInPath(name);
            if (inPath != null && !foundList.Contains(inPath))
            {
                foundList.Add(inPath);
            }
        }

        var newEngines = new List<EngineInfo>();

        foreach (var path in foundList)
        {
            bool alreadyRegistered;
            lock (_lock)
            {
                alreadyRegistered = _engines.Any(e => e.ExecutablePath == path);
            }

            if (alreadyRegistered) continue;

            var probe = await ProbeEngineFileAsync(path);
            if (probe.Success)
            {
                string id = $"sys-{Path.GetFileName(path)}-{Guid.NewGuid():N}"[..18];
                var eng = new EngineInfo
                {
                    Id = id,
                    Name = probe.Name,
                    Author = probe.Author,
                    ExecutablePath = path,
                    IsInstalled = true,
                    IsCustom = true,
                    DateAdded = DateTime.UtcNow
                };

                lock (_lock)
                {
                    _engines.Add(eng);
                    newEngines.Add(eng);
                }
            }
        }

        if (newEngines.Count > 0)
        {
            SaveConfig();
            OnEnginesChanged?.Invoke();
        }

        return newEngines;
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
