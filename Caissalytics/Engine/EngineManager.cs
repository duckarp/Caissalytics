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
    private readonly HttpClient _httpClient;
    private readonly object _lock = new();

    public event Action? OnEnginesChanged;
    public event Action<StockfishUpdateInfo>? OnStockfishUpdateChanged;
    public StockfishUpdateInfo? CachedStockfishUpdate { get; private set; }

    public bool IsAnalyzing => _activeClient != null && _activeClient.IsRunning;
    public string? SyzygyPath => _syzygyPath;

    public EngineManager(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Caissalytics/1.0 (Desktop; Open Source)");
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _engineStorageDir = Path.Combine(localAppData, "Caissalytics", "engines");
        Directory.CreateDirectory(_engineStorageDir);
        _configFilePath = Path.Combine(_engineStorageDir, "engines_config.json");

        LoadConfig();
        ScanForInstalledEngines();
    }

    public EngineManager(string customStorageDir, HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Caissalytics/1.0 (Desktop; Open Source)");
        }

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
            var exeCandidates = files.Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
            return exeCandidates.FirstOrDefault(f => Path.GetFileName(f).StartsWith("stockfish", StringComparison.OrdinalIgnoreCase))
                ?? exeCandidates.FirstOrDefault();
        }

        var candidates = files.Where(f =>
            !f.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) &&
            !f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            !f.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) &&
            !f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
            !f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
            !f.EndsWith(".nnue", StringComparison.OrdinalIgnoreCase) &&
            !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();

        return candidates.FirstOrDefault(f => Path.GetFileName(f).StartsWith("stockfish", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
    }

    private static void ExtractArchive(string archivePath, string targetDir)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, targetDir, overwriteFiles: true);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                 archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            using var fileStream = File.OpenRead(archivePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzipStream, targetDir, overwriteFiles: true);
        }
        else
        {
            TarFile.ExtractToDirectory(archivePath, targetDir, overwriteFiles: true);
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
        string ext = ".zip";
        if (downloadUrl.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) ext = ".tar.gz";
        else if (downloadUrl.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)) ext = ".tgz";
        else if (downloadUrl.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) ext = ".tar";
        string tempArchive = Path.Combine(targetDir, $"engine_download{ext}");

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

            ExtractArchive(tempArchive, targetDir);

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
        string? binaryToDelete = null;
        string? dirToDelete = null;
        bool wasActive = false;

        lock (_lock)
        {
            var eng = _engines.FirstOrDefault(e => e.Id == engineId);
            // Baseline stockfish-17 cannot be removed
            if (eng == null || eng.Id == "stockfish-17")
            {
                return Task.FromResult(false);
            }

            _engines.Remove(eng);

            if (_activeEngineId == engineId)
            {
                wasActive = true;
                _activeEngineId = _engines.FirstOrDefault(e => e.IsInstalled)?.Id ?? "stockfish-17";
            }

            UpdateActiveFlag();
            SaveConfig();

            // Check if engine files were stored inside Caissalytics managed engines directory
            if (!string.IsNullOrEmpty(eng.ExecutablePath) &&
                eng.ExecutablePath.StartsWith(_engineStorageDir, StringComparison.OrdinalIgnoreCase))
            {
                var parentDir = Path.GetDirectoryName(eng.ExecutablePath);
                if (!string.IsNullOrEmpty(parentDir) &&
                    parentDir.StartsWith(_engineStorageDir, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(parentDir, _engineStorageDir, StringComparison.OrdinalIgnoreCase))
                {
                    dirToDelete = parentDir;
                }
                else
                {
                    binaryToDelete = eng.ExecutablePath;
                }
            }
        }

        // Clean up managed files from disk outside lock
        if (dirToDelete != null && Directory.Exists(dirToDelete))
        {
            try
            {
                Directory.Delete(dirToDelete, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EngineManager] Error deleting engine dir: {ex.Message}");
            }
        }
        else if (binaryToDelete != null && File.Exists(binaryToDelete))
        {
            try
            {
                File.Delete(binaryToDelete);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EngineManager] Error deleting engine file: {ex.Message}");
            }
        }

        // Stop active UCI client if it was running the removed engine
        if (wasActive && _activeClient != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _activeClient.StopAnalysisAsync();
                    _activeClient.Dispose();
                    _activeClient = null;
                }
                catch { }
            });
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
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Chess", "Engines"),
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

    public async Task<StockfishUpdateInfo> CheckStockfishUpdateAsync(bool force = false, CancellationToken ct = default)
    {
        if (!force && CachedStockfishUpdate != null && CachedStockfishUpdate.CheckedAt.HasValue &&
            DateTime.UtcNow - CachedStockfishUpdate.CheckedAt.Value < TimeSpan.FromHours(1))
        {
            return CachedStockfishUpdate;
        }

        // Identify currently installed Stockfish
        EngineInfo? currentSf;
        lock (_lock)
        {
            currentSf = _engines
                .Where(e => e.IsInstalled && e.Id.StartsWith("stockfish", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => StockfishVersionHelper.ParseStockfishVersion(e.Version ?? e.Name))
                .FirstOrDefault();
        }

        double currentVerNum = currentSf != null ? StockfishVersionHelper.ParseStockfishVersion(currentSf.Version ?? currentSf.Name) : 0;
        string currentVerStr = currentSf != null ? (!string.IsNullOrEmpty(currentSf.Name) ? currentSf.Name : $"v{currentSf.Version}") : "Not installed";

        var updateInfo = new StockfishUpdateInfo
        {
            CurrentVersion = currentVerStr,
            IsChecking = true,
            StatusMessage = "Checking official Stockfish repository..."
        };

        CachedStockfishUpdate = updateInfo;
        OnStockfishUpdateChanged?.Invoke(updateInfo);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/official-stockfish/Stockfish/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github.v3+json");

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                updateInfo.IsChecking = false;
                updateInfo.StatusMessage = $"Unable to check Stockfish updates (HTTP {(int)response.StatusCode}).";
                updateInfo.CheckedAt = DateTime.UtcNow;
                OnStockfishUpdateChanged?.Invoke(updateInfo);
                return updateInfo;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tProp) ? tProp.GetString() ?? "" : "";
            string releaseTitle = root.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
            string releaseNotes = root.TryGetProperty("body", out var bProp) ? bProp.GetString() ?? "" : "";

            var assetTuples = new List<StockfishReleaseAsset>();
            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assetsElem.EnumerateArray())
                {
                    string aName = a.TryGetProperty("name", out var nameP) ? nameP.GetString() ?? "" : "";
                    string aUrl = a.TryGetProperty("browser_download_url", out var urlP) ? urlP.GetString() ?? "" : "";
                    long aSize = a.TryGetProperty("size", out var sizeP) ? sizeP.GetInt64() : 0;
                    if (!string.IsNullOrEmpty(aName) && !string.IsNullOrEmpty(aUrl))
                    {
                        assetTuples.Add(new StockfishReleaseAsset(aName, aUrl, aSize));
                    }
                }
            }

            var bestAsset = StockfishVersionHelper.SelectBestAsset(assetTuples);
            double latestVerNum = StockfishVersionHelper.ParseStockfishVersion(!string.IsNullOrEmpty(releaseTitle) ? releaseTitle : tagName);

            bool isUpdateAvailable = (currentSf != null && currentSf.IsInstalled) && (latestVerNum > currentVerNum);

            updateInfo.LatestVersion = !string.IsNullOrEmpty(releaseTitle) ? releaseTitle : tagName;
            updateInfo.ReleaseTag = tagName;
            updateInfo.ReleaseTitle = releaseTitle;
            updateInfo.ReleaseNotes = releaseNotes;
            updateInfo.IsUpdateAvailable = isUpdateAvailable;
            updateInfo.IsChecking = false;
            updateInfo.CheckedAt = DateTime.UtcNow;

            if (bestAsset != null)
            {
                updateInfo.AssetName = bestAsset.Name;
                updateInfo.DownloadUrl = bestAsset.Url;
                updateInfo.AssetSizeBytes = bestAsset.Size;
            }

            if (isUpdateAvailable)
            {
                updateInfo.StatusMessage = $"New release {updateInfo.LatestVersion} available!";
            }
            else if (currentSf != null && currentSf.IsInstalled)
            {
                updateInfo.StatusMessage = "Stockfish is up to date.";
            }
            else
            {
                updateInfo.StatusMessage = $"{updateInfo.LatestVersion} ready to install.";
            }

            CachedStockfishUpdate = updateInfo;
            OnStockfishUpdateChanged?.Invoke(updateInfo);
            return updateInfo;
        }
        catch (Exception ex)
        {
            updateInfo.IsChecking = false;
            updateInfo.StatusMessage = $"Could not check Stockfish updates: {ex.Message}";
            updateInfo.CheckedAt = DateTime.UtcNow;
            CachedStockfishUpdate = updateInfo;
            OnStockfishUpdateChanged?.Invoke(updateInfo);
            return updateInfo;
        }
    }

    public async Task<bool> UpdateStockfishAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var update = CachedStockfishUpdate;
        if (update == null || string.IsNullOrEmpty(update.DownloadUrl))
        {
            update = await CheckStockfishUpdateAsync(force: true, ct);
        }

        if (string.IsNullOrEmpty(update.DownloadUrl))
        {
            return false;
        }

        progress?.Report(5);

        double versionNum = StockfishVersionHelper.ParseStockfishVersion(update.LatestVersion);
        int major = versionNum > 0 ? (int)Math.Floor(versionNum) : 19;
        string engineId = $"stockfish-{major}";
        string targetDir = Path.Combine(_engineStorageDir, engineId);
        Directory.CreateDirectory(targetDir);

        string ext = ".zip";
        if (update.DownloadUrl.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) ext = ".tar.gz";
        else if (update.DownloadUrl.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)) ext = ".tgz";
        else if (update.DownloadUrl.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) ext = ".tar";

        string tempArchive = Path.Combine(targetDir, $"stockfish_download{ext}");

        try
        {
            progress?.Report(10);

            using (var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                using var fs = new FileStream(tempArchive, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs, ct);
            }

            progress?.Report(60);

            ExtractArchive(tempArchive, targetDir);

            try { File.Delete(tempArchive); } catch { }

            progress?.Report(80);

            var binary = FindExecutableInDirectory(targetDir);
            if (binary == null)
            {
                Console.WriteLine($"[EngineManager] Executable not found in {targetDir}");
                return false;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    File.SetUnixFileMode(binary,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch { }
            }

            progress?.Report(90);

            var probe = await ProbeEngineFileAsync(binary);
            string finalName = probe.Success && !string.IsNullOrEmpty(probe.Name)
                ? probe.Name
                : (!string.IsNullOrEmpty(update.ReleaseTitle) ? update.ReleaseTitle : $"Stockfish {major}");

            lock (_lock)
            {
                var existing = _engines.FirstOrDefault(e => e.Id == engineId);
                if (existing != null)
                {
                    existing.Name = finalName;
                    existing.ExecutablePath = binary;
                    existing.IsInstalled = true;
                    existing.Version = $"{major}.0";
                    existing.Author = !string.IsNullOrEmpty(probe.Author) ? probe.Author : "The Stockfish Developers";
                    existing.DownloadUrl = update.DownloadUrl;
                }
                else
                {
                    var newEng = new EngineInfo
                    {
                        Id = engineId,
                        Name = finalName,
                        Version = $"{major}.0",
                        Author = !string.IsNullOrEmpty(probe.Author) ? probe.Author : "The Stockfish Developers",
                        ExecutablePath = binary,
                        DownloadUrl = update.DownloadUrl,
                        IsInstalled = true,
                        IsCustom = false,
                        DateAdded = DateTime.UtcNow
                    };
                    _engines.Insert(0, newEng);
                }

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

            update.IsUpdateAvailable = false;
            update.CurrentVersion = finalName;
            update.StatusMessage = $"Stockfish successfully updated to {finalName}!";
            CachedStockfishUpdate = update;

            progress?.Report(100);

            OnEnginesChanged?.Invoke();
            OnStockfishUpdateChanged?.Invoke(update);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EngineManager] Failed to update Stockfish: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _activeClient?.Dispose();
        _activeClient = null;
        _httpClient.Dispose();
    }
}
