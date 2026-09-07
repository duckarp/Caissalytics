using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Caissalytics.Data;

public class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly string _settingsPath;
    private UpdateSettings _settings = new();
    private readonly object _lock = new();

    public event Action<UpdateInfo>? OnUpdateStateChanged;
    public UpdateInfo? CachedUpdate { get; private set; }
    private string? _stagedFilePath;

    public UpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Caissalytics-Desktop/1.0");
        }

        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Caissalytics");
        Directory.CreateDirectory(baseDir);
        _settingsPath = Path.Combine(baseDir, "update_settings.json");

        LoadSettings();
    }

    public string GetCurrentVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVer))
        {
            return SemVerHelper.CleanVersion(infoVer);
        }
        var ver = asm.GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";
    }

    public Task<UpdateSettings> GetSettingsAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(new UpdateSettings
            {
                Repository = _settings.Repository,
                AutoCheckOnStartup = _settings.AutoCheckOnStartup,
                CheckPrereleases = _settings.CheckPrereleases,
                LastCheckedAt = _settings.LastCheckedAt
            });
        }
    }

    public Task SaveSettingsAsync(UpdateSettings settings)
    {
        lock (_lock)
        {
            _settings = settings;
            try
            {
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Error saving settings: {ex.Message}");
            }
        }
        return Task.CompletedTask;
    }

    private void LoadSettings()
    {
        lock (_lock)
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsPath);
                    var s = JsonSerializer.Deserialize<UpdateSettings>(json);
                    if (s != null) _settings = s;
                }
                catch { }
            }
        }
    }

    public async Task<UpdateInfo> CheckForUpdatesAsync(bool force = false, CancellationToken ct = default)
    {
        string currentVer = GetCurrentVersion();
        string repo = string.IsNullOrWhiteSpace(_settings.Repository) ? "tomask/Caissalytics" : _settings.Repository.Trim();

        var updateInfo = new UpdateInfo
        {
            CurrentVersion = currentVer,
            StatusMessage = "Checking for updates..."
        };

        try
        {
            string url = _settings.CheckPrereleases
                ? $"https://api.github.com/repos/{repo}/releases"
                : $"https://api.github.com/repos/{repo}/releases/latest";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/vnd.github.v3+json");

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                updateInfo.StatusMessage = $"Update server returned status {response.StatusCode}.";
                CachedUpdate = updateInfo;
                OnUpdateStateChanged?.Invoke(updateInfo);
                return updateInfo;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            JsonElement releaseElement;
            if (_settings.CheckPrereleases && doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                if (doc.RootElement.GetArrayLength() == 0)
                {
                    updateInfo.StatusMessage = "No releases found.";
                    CachedUpdate = updateInfo;
                    OnUpdateStateChanged?.Invoke(updateInfo);
                    return updateInfo;
                }
                releaseElement = doc.RootElement[0];
            }
            else
            {
                releaseElement = doc.RootElement;
            }

            ParseReleaseElement(releaseElement, updateInfo, currentVer);

            lock (_lock)
            {
                _settings.LastCheckedAt = DateTime.UtcNow;
                _ = SaveSettingsAsync(_settings);
            }

            CachedUpdate = updateInfo;
            OnUpdateStateChanged?.Invoke(updateInfo);
            return updateInfo;
        }
        catch (Exception ex)
        {
            updateInfo.StatusMessage = $"Unable to check for updates: {ex.Message}";
            CachedUpdate = updateInfo;
            OnUpdateStateChanged?.Invoke(updateInfo);
            return updateInfo;
        }
    }

    public static void ParseReleaseElement(JsonElement releaseElement, UpdateInfo updateInfo, string currentVer)
    {
        string tag = releaseElement.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
        string name = releaseElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
        string body = releaseElement.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
        string htmlUrl = releaseElement.TryGetProperty("html_url", out var htmlProp) ? htmlProp.GetString() ?? "" : "";
        DateTime? publishedAt = releaseElement.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var dt) ? dt : null;

        updateInfo.TagName = tag;
        updateInfo.LatestVersion = SemVerHelper.CleanVersion(tag);
        updateInfo.ReleaseTitle = string.IsNullOrWhiteSpace(name) ? tag : name;
        updateInfo.ReleaseNotes = body;
        updateInfo.ReleaseUrl = htmlUrl;
        updateInfo.PublishedAt = publishedAt;

        // Determine if newer
        updateInfo.IsUpdateAvailable = SemVerHelper.IsNewerVersion(updateInfo.LatestVersion, currentVer);
        updateInfo.StatusMessage = updateInfo.IsUpdateAvailable
            ? $"New version {updateInfo.LatestVersion} is available!"
            : $"You are up to date ({currentVer}).";

        // Find matching OS asset
        if (releaseElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            PickBestAsset(assets, updateInfo);
        }
    }

    public static void PickBestAsset(JsonElement assets, UpdateInfo updateInfo)
    {
        bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        JsonElement? bestAsset = null;
        int bestScore = -1;

        foreach (var asset in assets.EnumerateArray())
        {
            string assetName = asset.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";
            int score = 0;

            if (isWin)
            {
                if (assetName.Contains("win", StringComparison.OrdinalIgnoreCase)) score += 10;
                if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) score += 5;
                if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) score += 4;
            }
            else if (isLinux)
            {
                if (assetName.Contains("linux", StringComparison.OrdinalIgnoreCase)) score += 10;
                if (assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) score += 5;
                if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) score += 4;
                if (assetName.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)) score += 6;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestAsset = asset;
            }
        }

        // Fallback to first asset if none scored platform match
        if (bestAsset == null && assets.GetArrayLength() > 0)
        {
            bestAsset = assets[0];
        }

        if (bestAsset.HasValue)
        {
            var el = bestAsset.Value;
            updateInfo.AssetFileName = el.TryGetProperty("name", out var n) ? n.GetString() : null;
            updateInfo.AssetDownloadUrl = el.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() : null;
            updateInfo.AssetSizeBytes = el.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
        }
    }

    public async Task DownloadUpdateAsync(
        UpdateInfo update,
        IProgress<(int current, int total, string status)>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(update.AssetDownloadUrl))
        {
            throw new InvalidOperationException("No download asset is available for this release.");
        }

        string updateDir = Path.Combine(Path.GetTempPath(), "Caissalytics_Update");
        Directory.CreateDirectory(updateDir);

        string fileName = update.AssetFileName ?? "update_package.zip";
        string targetFile = Path.Combine(updateDir, fileName);

        progress?.Report((5, 100, $"Connecting to download server..."));

        using var request = new HttpRequestMessage(HttpMethod.Get, update.AssetDownloadUrl);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? update.AssetSizeBytes;
        long bytesRead = 0;

        using (var source = await response.Content.ReadAsStreamAsync(ct))
        using (var dest = File.Create(targetFile))
        {
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await dest.WriteAsync(buffer, 0, read, ct);
                bytesRead += read;

                if (totalBytes > 0)
                {
                    int pct = Math.Clamp((int)(bytesRead * 100 / totalBytes), 5, 95);
                    progress?.Report((pct, 100, $"Downloading update: {bytesRead / 1024.0 / 1024.0:F1} MB of {totalBytes / 1024.0 / 1024.0:F1} MB..."));
                }
            }
        }

        _stagedFilePath = targetFile;
        progress?.Report((100, 100, "Download complete! Ready to apply and restart."));
    }

    public Task ApplyUpdateAndRestartAsync()
    {
        if (string.IsNullOrEmpty(_stagedFilePath) || !File.Exists(_stagedFilePath))
        {
            throw new FileNotFoundException("Staged update package not found. Please download the update first.");
        }

        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
        {
            currentExe = Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (string.IsNullOrEmpty(currentExe))
        {
            throw new InvalidOperationException("Could not determine current executable path.");
        }

        string appDir = Path.GetDirectoryName(currentExe) ?? AppContext.BaseDirectory;
        int pid = Environment.ProcessId;
        string stagingDir = Path.GetDirectoryName(_stagedFilePath)!;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string batScript = Path.Combine(stagingDir, "apply_update.bat");
            string scriptContent = $@"@echo off
set PID={pid}
set APP_DIR=""{appDir}""
set ARCHIVE=""{_stagedFilePath}""
set EXE=""{currentExe}""

:waitloop
tasklist /fi ""PID eq %PID%"" 2>nul | find ""%PID%"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto waitloop
)

tar -xf %ARCHIVE% -C %APP_DIR% >nul 2>&1
start """" %EXE%
del ""%~f0""
";
            File.WriteAllText(batScript, scriptContent);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batScript}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WorkingDirectory = stagingDir
            };
            Process.Start(psi);
            Environment.Exit(0);
        }
        else
        {
            string shScript = Path.Combine(stagingDir, "apply_update.sh");
            string scriptContent = $@"#!/bin/sh
PID={pid}
APP_DIR=""{appDir}""
ARCHIVE=""{_stagedFilePath}""
EXE=""{currentExe}""

while kill -0 $PID 2>/dev/null; do
    sleep 1
done

unzip -o ""$ARCHIVE"" -d ""$APP_DIR"" 2>/dev/null || tar -xzf ""$ARCHIVE"" -C ""$APP_DIR"" 2>/dev/null
chmod +x ""$EXE""
""$EXE"" &
rm -f ""$0""
";
            File.WriteAllText(shScript, scriptContent);
            try { Process.Start("chmod", $"+x \"{shScript}\"").WaitForExit(); } catch { }

            var psi = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"\"{shScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = stagingDir
            };
            Process.Start(psi);
            Environment.Exit(0);
        }

        return Task.CompletedTask;
    }
}
