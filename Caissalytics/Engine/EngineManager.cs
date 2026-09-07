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
    private string? _syzygyPath;
    private UciProcessClient? _activeClient;
    private readonly HttpClient _httpClient = new();
    private readonly object _lock = new();

    public event Action? OnEnginesChanged;

    public bool IsAnalyzing => _activeClient != null && _activeClient.IsRunning;
    public string? SyzygyPath => _syzygyPath;

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
                        _syzygyPath = config.SyzygyPath;
                        _engines.AddRange(config.Engines);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EngineManager] Error loading config: {ex.Message}");
                }
            }

            // Ensure official Stockfish 17 is always in the registry
            var sf17 = _engines.FirstOrDefault(e => e.Id == "stockfish-17");
            if (sf17 == null)
            {
                _engines.Insert(0, new EngineInfo
                {
                    Id = "stockfish-17",
                    Name = "Stockfish 17",
                    Version = "17.0",
                    Author = "The Stockfish Developers",
                    DownloadUrl = GetDefaultStockfishDownloadUrl(),
                    IsCustom = false
                });
            }
            else if (!sf17.IsInstalled)
            {
                // Keep URL aligned with current platform if not yet installed
                sf17.DownloadUrl = GetDefaultStockfishDownloadUrl();
            }

            UpdateActiveFlag();
        }
    }

    public static string GetDefaultStockfishDownloadUrl()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "https://github.com/official-stockfish/Stockfish/releases/download/sf_17/stockfish-windows-x86-64-avx2.zip";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "https://github.com/official-stockfish/Stockfish/releases/download/sf_17/stockfish-macos-m1-apple-silicon.tar";
        }
        return "https://github.com/official-stockfish/Stockfish/releases/download/sf_17/stockfish-ubuntu-x86-64-avx2.tar";
    }

    private static string? FindExecutableInDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return null;

        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return files.FirstOrDefault(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        }

        return files.FirstOrDefault(f =>
            !f.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) &&
            !f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            !f.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) &&
            !f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
            !f.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
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
                    SyzygyPath = _syzygyPath,
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

    public async Task SetSyzygyPathAsync(string? path)
    {
        lock (_lock)
        {
            _syzygyPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            SaveConfig();
        }

        if (_activeClient != null && _activeClient.IsRunning && !string.IsNullOrWhiteSpace(_syzygyPath))
        {
            try
            {
                await _activeClient.SetOptionAsync("SyzygyPath", _syzygyPath);
            }
            catch
            {
                // Engine might not support option or exited
            }
        }

        OnEnginesChanged?.Invoke();
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
                    var binary = FindExecutableInDirectory(engineDir);
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

        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };

        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paths)
        {
            foreach (var ext in extensions)
            {
                string fullName = binaryName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                    ? binaryName
                    : binaryName + ext;
                string full = Path.Combine(p, fullName);
                if (File.Exists(full))
                    return full;
            }
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

        string downloadUrl = engine.DownloadUrl;
        if (engine.Id == "stockfish-17")
        {
            downloadUrl = GetDefaultStockfishDownloadUrl();
        }

        string targetDir = Path.Combine(_engineStorageDir, engine.Id);
        Directory.CreateDirectory(targetDir);
        bool isZip = downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        string tempArchive = Path.Combine(targetDir, isZip ? "engine_download.zip" : "engine_download.tar");

        try
        {
            progress?.Report(10);

            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using var fs = new FileStream(tempArchive, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);
            }

            progress?.Report(60);

            if (isZip)
            {
                ZipFile.ExtractToDirectory(tempArchive, targetDir, overwriteFiles: true);
            }
            else
            {
                TarFile.ExtractToDirectory(tempArchive, targetDir, overwriteFiles: true);
            }

            try { File.Delete(tempArchive); } catch { }

            progress?.Report(85);

            var binary = FindExecutableInDirectory(targetDir);
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
        var candidateDirs = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ChessBase", "Engines"),
                @"C:\Chess",
                @"C:\Engines"
            }
            : new[] { "/usr/games", "/usr/bin", "/usr/local/bin", "/opt/chess" };
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
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    string exePath = Path.Combine(dir, name + ".exe");
                    if (File.Exists(exePath) && !foundList.Contains(exePath))
                    {
                        foundList.Add(exePath);
                    }
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

            if (!string.IsNullOrWhiteSpace(_syzygyPath))
            {
                try
                {
                    await _activeClient.SetOptionAsync("SyzygyPath", _syzygyPath);
                }
                catch { }
            }
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
