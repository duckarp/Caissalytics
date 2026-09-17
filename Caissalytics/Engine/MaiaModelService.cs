using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Caissalytics.Engine;

public class MaiaModelService : IMaiaModelService
{
    private readonly string _modelsDirectory;
    private readonly string _lc0Directory;
    private readonly HttpClient _httpClient;
    private readonly IEngineService? _engineService;
    private readonly List<MaiaModelInfo> _models = new();
    private readonly object _lock = new();

    public event Action? OnModelsChanged;

    public IReadOnlyList<MaiaModelInfo> Models
    {
        get
        {
            lock (_lock)
            {
                return _models.ToList();
            }
        }
    }

    public string ModelsDirectory => _modelsDirectory;

    public string? CustomLc0Path { get; set; }

    public bool IsLc0Available => FindLc0Executable() != null;

    public MaiaModelService(IEngineService? engineService = null, HttpClient? httpClient = null, string? customStorageDir = null)
    {
        _engineService = engineService;
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Caissalytics/1.0 (Desktop; Open Source)");
        }

        string baseDir = customStorageDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Caissalytics",
            "engines"
        );

        _modelsDirectory = Path.Combine(baseDir, "maia");
        _lc0Directory = Path.Combine(baseDir, "lc0");
        _configFilePath = Path.Combine(baseDir, "maia_config.json");

        Directory.CreateDirectory(_modelsDirectory);
        Directory.CreateDirectory(_lc0Directory);

        InitializeModels();
        ScanExistingModels();
        LoadConfig();
    }

    private readonly string _configFilePath;

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
                if (dict != null && dict.TryGetValue("CustomLc0Path", out var path) && !string.IsNullOrWhiteSpace(path))
                {
                    CustomLc0Path = path;
                }
            }
        }
        catch { }
    }

    public void SaveConfig()
    {
        try
        {
            var dict = new Dictionary<string, string?>
            {
                ["CustomLc0Path"] = CustomLc0Path
            };
            File.WriteAllText(_configFilePath, System.Text.Json.JsonSerializer.Serialize(dict));
        }
        catch { }
    }

    private void InitializeModels()
    {
        int[] ratings = [1100, 1200, 1300, 1400, 1500, 1600, 1700, 1800, 1900];
        foreach (int r in ratings)
        {
            string fileName = $"maia-{r}.pb.gz";
            _models.Add(new MaiaModelInfo
            {
                Rating = r,
                Name = $"Maia {r}",
                WeightsFileName = fileName,
                DownloadUrl = $"https://github.com/CSSLab/maia-chess/releases/download/v1.0/{fileName}"
            });
        }
    }

    private void ScanExistingModels()
    {
        lock (_lock)
        {
            foreach (var model in _models)
            {
                string path = Path.Combine(_modelsDirectory, model.WeightsFileName);
                if (File.Exists(path))
                {
                    model.FilePath = path;
                    model.FileSizeBytes = new FileInfo(path).Length;
                }
                else
                {
                    model.FilePath = null;
                    model.FileSizeBytes = 0;
                }
            }
        }
    }

    public MaiaModelInfo? GetModel(int rating)
    {
        lock (_lock)
        {
            return _models.FirstOrDefault(m => m.Rating == rating);
        }
    }

    public MaiaModelInfo GetClosestModel(int rating)
    {
        lock (_lock)
        {
            return _models.OrderBy(m => Math.Abs(m.Rating - rating)).First();
        }
    }

    public string? FindLc0Executable()
    {
        // 1. Custom user path
        if (!string.IsNullOrWhiteSpace(CustomLc0Path) && File.Exists(CustomLc0Path))
        {
            return CustomLc0Path;
        }

        // 2. Managed lc0 directory
        if (Directory.Exists(_lc0Directory))
        {
            string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "lc0.exe" : "lc0";
            var direct = Path.Combine(_lc0Directory, exeName);
            if (File.Exists(direct)) return direct;

            // Search child directories (e.g. if extracted from zip)
            var found = Directory.GetFiles(_lc0Directory, exeName, SearchOption.AllDirectories).FirstOrDefault();
            if (found != null) return found;
        }

        // 3. System PATH / common Unix locations
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string[] commonPaths = ["/usr/bin/lc0", "/usr/local/bin/lc0", "/opt/lc0/lc0", "/usr/games/lc0"];
            foreach (var p in commonPaths)
            {
                if (File.Exists(p)) return p;
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(':', StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        var candidate = Path.Combine(dir, "lc0");
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { }
                }
            }
        }

        // 4. Check if registered as a custom engine in EngineManager
        if (_engineService != null)
        {
            var engines = _engineService.GetEnginesAsync().GetAwaiter().GetResult();
            var lc0Engine = engines.FirstOrDefault(e =>
                e.IsInstalled &&
                !string.IsNullOrEmpty(e.ExecutablePath) &&
                (e.Name.Contains("lc0", StringComparison.OrdinalIgnoreCase) ||
                 e.Name.Contains("leela", StringComparison.OrdinalIgnoreCase) ||
                 Path.GetFileNameWithoutExtension(e.ExecutablePath).Equals("lc0", StringComparison.OrdinalIgnoreCase)));

            if (lc0Engine != null && File.Exists(lc0Engine.ExecutablePath))
            {
                return lc0Engine.ExecutablePath;
            }
        }

        return null;
    }

    public async Task<bool> DownloadModelAsync(int rating, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var model = GetModel(rating);
        if (model == null) return false;

        string targetPath = Path.Combine(_modelsDirectory, model.WeightsFileName);
        string tempPath = targetPath + ".download";

        try
        {
            progress?.Report(10);

            using var response = await _httpClient.GetAsync(model.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[81920];
            long downloaded = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloaded += bytesRead;
                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    int pct = (int)((downloaded * 80) / totalBytes.Value) + 10;
                    progress?.Report(Math.Min(95, pct));
                }
            }

            await fs.FlushAsync(ct);
            fs.Close();

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);

            lock (_lock)
            {
                model.FilePath = targetPath;
                model.FileSizeBytes = new FileInfo(targetPath).Length;
            }

            progress?.Report(100);
            OnModelsChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaiaModelService] Error downloading {model.Name}: {ex.Message}");
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return false;
        }
    }

    public async Task<bool> DownloadAllModelsAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        int total = _models.Count;
        int completed = 0;

        foreach (var m in _models)
        {
            if (ct.IsCancellationRequested) return false;

            if (m.IsDownloaded)
            {
                completed++;
                progress?.Report((completed * 100) / total);
                continue;
            }

            var subProgress = new Progress<int>(p =>
            {
                int overall = ((completed * 100) + p) / total;
                progress?.Report(overall);
            });

            bool ok = await DownloadModelAsync(m.Rating, subProgress, ct);
            if (!ok) return false;

            completed++;
            progress?.Report((completed * 100) / total);
        }

        return true;
    }

    public string? LastInstallError { get; private set; }

    public async Task<bool> EnsureLc0Async(IProgress<(string phase, int pct)>? progress = null, CancellationToken ct = default)
    {
        LastInstallError = null;

        if (IsLc0Available) return true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: download prebuilt binary from GitHub releases
            var intProgress = new Progress<int>(p => progress?.Report(("Downloading lc0...", p)));
            bool ok = await EnsureWindowsLc0Async(intProgress, ct);
            if (!ok) LastInstallError = "Failed to download lc0 for Windows. Check your internet connection.";
            return ok;
        }
        else
        {
            // Linux / macOS: build from source (universal, no distro-specific tools)
            return await BuildLc0FromSourceAsync(progress, ct);
        }
    }

    private async Task<bool> BuildLc0FromSourceAsync(IProgress<(string phase, int pct)>? progress, CancellationToken ct)
    {
        string srcDir   = Path.Combine(_lc0Directory, "src");
        string buildDir = Path.Combine(_lc0Directory, "build");
        string targetBin = Path.Combine(_lc0Directory, "lc0");

        // Step 1 – git clone
        progress?.Report(("Cloning lc0 repository...", 5));
        bool cloneOk = await RunProcessAsync("git",
            $"clone --depth 1 -b release/0.32 https://github.com/LeelaChessZero/lc0.git \"{srcDir}\"",
            workingDir: _lc0Directory, ct: ct);
        if (!cloneOk)
        {
            // If src dir already exists from a previous attempt, try to pull instead
            if (Directory.Exists(srcDir))
            {
                progress?.Report(("Updating lc0 repository...", 5));
                cloneOk = await RunProcessAsync("git", "pull --ff-only", workingDir: srcDir, ct: ct);
            }
            if (!cloneOk)
            {
                LastInstallError = "git clone failed. Make sure 'git' is installed and you have internet access.";
                return false;
            }
        }

        // Step 2 – meson setup
        progress?.Report(("Configuring build (meson)...", 25));
        // Remove stale build dir to avoid meson "already configured" errors
        if (Directory.Exists(buildDir))
        {
            try { Directory.Delete(buildDir, recursive: true); } catch { }
        }
        bool mesonOk = await RunProcessAsync("meson",
            $"setup \"{buildDir}\" \"{srcDir}\" --buildtype=release",
            workingDir: _lc0Directory, ct: ct);
        if (!mesonOk)
        {
            LastInstallError = "meson setup failed. Make sure 'meson', 'ninja', and 'g++' are installed.";
            return false;
        }

        // Step 3 – ninja build
        progress?.Report(("Building lc0 (this may take 1–3 minutes)...", 45));
        bool ninjaOk = await RunProcessAsync("ninja",
            $"-C \"{buildDir}\" lc0",
            workingDir: _lc0Directory, ct: ct);
        if (!ninjaOk)
        {
            LastInstallError = "ninja build failed. Make sure 'ninja' and 'g++' are installed.";
            return false;
        }

        // Step 4 – copy binary
        progress?.Report(("Copying binary...", 92));
        string builtBin = Path.Combine(buildDir, "lc0");
        if (!File.Exists(builtBin))
        {
            LastInstallError = "Build appeared to succeed but lc0 binary was not found.";
            return false;
        }

        try
        {
            if (File.Exists(targetBin)) File.Delete(targetBin);
            File.Copy(builtBin, targetBin);
            // Make executable
            await RunProcessAsync("chmod", $"+x \"{targetBin}\"", workingDir: _lc0Directory, ct: ct);
        }
        catch (Exception ex)
        {
            LastInstallError = $"Failed to copy lc0 binary: {ex.Message}";
            return false;
        }

        progress?.Report(("Done!", 100));
        OnModelsChanged?.Invoke();
        return IsLc0Available;
    }

    /// <summary>Runs a process and waits for exit. Returns true if exit code is 0.</summary>
    private static async Task<bool> RunProcessAsync(string fileName, string arguments, string workingDir, CancellationToken ct)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute = false,
                CreateNoWindow  = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return false;

            // Drain stdout/stderr to prevent deadlock on large output
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(ct);

            await proc.WaitForExitAsync(ct);
            Console.WriteLine($"[MaiaModelService] {fileName} exited with {proc.ExitCode}");
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaiaModelService] {fileName} failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> EnsureWindowsLc0Async(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return IsLc0Available;
        }

        if (IsLc0Available) return true;

        string downloadUrl = "https://github.com/LeelaChessZero/lc0/releases/download/v0.32.1/lc0-v0.32.1-windows-cpu-openblas.zip";
        string targetZip = Path.Combine(_lc0Directory, "lc0-windows.zip");

        try
        {
            progress?.Report(10);
            using (var resp = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                using var fs = new FileStream(targetZip, FileMode.Create, FileAccess.Write, FileShare.None);
                await resp.Content.CopyToAsync(fs, ct);
            }

            progress?.Report(70);
            ZipFile.ExtractToDirectory(targetZip, _lc0Directory, overwriteFiles: true);
            try { File.Delete(targetZip); } catch { }

            progress?.Report(100);
            return IsLc0Available;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaiaModelService] Error downloading Windows lc0: {ex.Message}");
            return false;
        }
    }
}
