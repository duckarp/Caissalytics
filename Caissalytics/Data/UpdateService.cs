using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

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
        var asm = typeof(UpdateService).Assembly;
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
        string repo = string.IsNullOrWhiteSpace(_settings.Repository) ? "duckarp/Caissalytics" : _settings.Repository.Trim();

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

            if (!string.IsNullOrWhiteSpace(updateInfo.TagName))
            {
                await EnrichChangelogAsync(repo, updateInfo, currentVer, ct);
            }

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

        // Check for compare URL in body (e.g. "**Full Changelog**: https://github.com/duckarp/Caissalytics/compare/v1.7.3...v1.7.4")
        if (!string.IsNullOrWhiteSpace(body))
        {
            var match = Regex.Match(
                body,
                @"https://github\.com/[^/\s\)]+/[^/\s\)]+/compare/([^\s\)\r\n]+?)\.\.\.([^\s\)\r\n]+)");
            if (match.Success)
            {
                updateInfo.CompareUrl = match.Value;
            }
        }

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

    private async Task EnrichChangelogAsync(string repo, UpdateInfo updateInfo, string currentVer, CancellationToken ct)
    {
        string headTag = updateInfo.TagName;
        if (string.IsNullOrWhiteSpace(headTag)) return;

        string? fallbackBaseTag = null;
        if (!string.IsNullOrWhiteSpace(updateInfo.CompareUrl))
        {
            var match = Regex.Match(updateInfo.CompareUrl, @"compare/([^\s\)\r\n]+?)\.\.\.([^\s\)\r\n]+)");
            if (match.Success)
            {
                fallbackBaseTag = match.Groups[1].Value;
            }
        }

        string baseTag = (!string.IsNullOrWhiteSpace(currentVer) && currentVer != "0.0.0" && SemVerHelper.CleanVersion(currentVer) != updateInfo.LatestVersion)
            ? (currentVer.StartsWith('v') || currentVer.StartsWith('V') ? currentVer : $"v{currentVer}")
            : (fallbackBaseTag ?? "");

        if (string.Equals(baseTag, headTag, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(fallbackBaseTag) && !string.Equals(fallbackBaseTag, headTag, StringComparison.OrdinalIgnoreCase))
        {
            baseTag = fallbackBaseTag;
        }

        if (!string.IsNullOrWhiteSpace(baseTag) && !string.Equals(baseTag, headTag, StringComparison.OrdinalIgnoreCase))
        {
            var commits = await FetchCompareCommitsAsync(repo, baseTag, headTag, ct);
            if ((commits == null || commits.Count == 0) && !string.IsNullOrEmpty(fallbackBaseTag) && fallbackBaseTag != baseTag)
            {
                commits = await FetchCompareCommitsAsync(repo, fallbackBaseTag, headTag, ct);
            }

            if (commits != null && commits.Count > 0)
            {
                var changelog = new List<ReleaseChangelogItem>();
                foreach (var c in commits)
                {
                    var item = ParseCommitMessage(c.Message, c.Sha, c.Author);
                    if (item != null)
                    {
                        changelog.Add(item);
                    }
                }

                updateInfo.ChangelogItems = changelog;
            }
        }

        string sanitized = SanitizeReleaseNotes(updateInfo.ReleaseNotes);
        updateInfo.AuthorNotes = sanitized;
        if (string.IsNullOrWhiteSpace(sanitized) && updateInfo.ChangelogItems.Count > 0)
        {
            updateInfo.ReleaseNotes = string.Join("\n", updateInfo.ChangelogItems.Select(ci => $"• {ci.FormattedText}"));
        }
        else
        {
            updateInfo.ReleaseNotes = sanitized;
        }
    }

    private async Task<List<(string Message, string Sha, string Author)>?> FetchCompareCommitsAsync(string repo, string baseTag, string headTag, CancellationToken ct)
    {
        try
        {
            string url = $"https://api.github.com/repos/{repo}/compare/{baseTag}...{headTag}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/vnd.github.v3+json");

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("commits", out var commitsArray) && commitsArray.ValueKind == JsonValueKind.Array)
            {
                var list = new List<(string Message, string Sha, string Author)>();
                foreach (var c in commitsArray.EnumerateArray())
                {
                    string sha = c.TryGetProperty("sha", out var shaProp) ? shaProp.GetString() ?? "" : "";
                    string msg = "";
                    string author = "";
                    if (c.TryGetProperty("commit", out var commitObj))
                    {
                        msg = commitObj.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "" : "";
                        if (commitObj.TryGetProperty("author", out var authObj))
                        {
                            author = authObj.TryGetProperty("name", out var aProp) ? aProp.GetString() ?? "" : "";
                        }
                    }
                    list.Add((msg, sha, author));
                }
                return list;
            }
        }
        catch
        {
            // Graceful fallback on network/rate-limit issues
        }
        return null;
    }

    public static bool IsIgnoredCommit(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return true;
        string firstLine = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(firstLine)) return true;

        if (firstLine.StartsWith("Merge pull request", StringComparison.OrdinalIgnoreCase)) return true;
        if (firstLine.StartsWith("Merge branch", StringComparison.OrdinalIgnoreCase)) return true;
        if (firstLine.StartsWith("Merge remote-tracking", StringComparison.OrdinalIgnoreCase)) return true;
        if (Regex.IsMatch(firstLine, @"^(?:chore(?:\([^)]+\))?:\s*)?(?:release|bump(?:\s+version)?(?:\s+to)?|version\s+bump)\s+v?\d+", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(firstLine, @"^(?:chore(?:\([^)]+\))?:\s*)?(?:version\s+bump|bump\s+version)", RegexOptions.IgnoreCase)) return true;

        return false;
    }

    public static ReleaseChangelogItem? ParseCommitMessage(string message, string sha = "", string author = "")
    {
        if (IsIgnoredCommit(message)) return null;

        string firstLine = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(firstLine)) return null;

        string shortSha = !string.IsNullOrEmpty(sha) && sha.Length >= 7 ? sha[..7] : sha;

        var match = Regex.Match(firstLine, @"^([a-zA-Z]+)(?:\(([^)]+)\))?\s*:\s*(.+)$");
        if (match.Success)
        {
            string rawType = match.Groups[1].Value.ToLowerInvariant();
            string rawScope = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";
            string desc = match.Groups[3].Value.Trim();

            if (desc.Length > 0 && char.IsLower(desc[0]))
            {
                desc = char.ToUpperInvariant(desc[0]) + desc[1..];
            }

            if (!string.IsNullOrEmpty(rawScope))
            {
                rawScope = char.ToUpperInvariant(rawScope[0]) + rawScope[1..];
            }

            string category = rawType switch
            {
                "feat" or "feature" => "Feature",
                "fix" or "bugfix" => "Fix",
                "perf" or "performance" => "Performance",
                "style" or "ui" => "UI / Design",
                "refactor" => "Refactor",
                "docs" or "doc" => "Docs",
                "test" or "tests" => "Tests",
                _ => "Update"
            };

            return new ReleaseChangelogItem
            {
                Category = category,
                Scope = rawScope,
                Description = desc,
                RawMessage = firstLine,
                CommitSha = shortSha,
                Author = author
            };
        }

        string fallbackDesc = firstLine;
        if (fallbackDesc.Length > 0 && char.IsLower(fallbackDesc[0]))
        {
            fallbackDesc = char.ToUpperInvariant(fallbackDesc[0]) + fallbackDesc[1..];
        }

        return new ReleaseChangelogItem
        {
            Category = "Update",
            Scope = "",
            Description = fallbackDesc,
            RawMessage = firstLine,
            CommitSha = shortSha,
            Author = author
        };
    }

    public static string SanitizeReleaseNotes(string? rawNotes)
    {
        if (string.IsNullOrWhiteSpace(rawNotes)) return string.Empty;

        var lines = rawNotes.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var filteredLines = new List<string>();

        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("**Full Changelog**", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Full Changelog:", StringComparison.OrdinalIgnoreCase) ||
                (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("/compare/")))
            {
                continue;
            }

            filteredLines.Add(line);
        }

        return string.Join("\n", filteredLines).Trim();
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
