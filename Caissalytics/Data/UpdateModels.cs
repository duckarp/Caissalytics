namespace Caissalytics.Data;

public class UpdateInfo
{
    public string CurrentVersion { get; set; } = "1.0.0";
    public string LatestVersion { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string ReleaseUrl { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
    public string? AssetDownloadUrl { get; set; }
    public string? AssetFileName { get; set; }
    public long AssetSizeBytes { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public string StatusMessage { get; set; } = string.Empty;

    public string FormattedAssetSize
    {
        get
        {
            if (AssetSizeBytes <= 0) return string.Empty;
            if (AssetSizeBytes < 1024 * 1024) return $"{AssetSizeBytes / 1024.0:F1} KB";
            return $"{AssetSizeBytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}

public class UpdateSettings
{
    public string Repository { get; set; } = "tomask/Caissalytics";
    public bool AutoCheckOnStartup { get; set; } = true;
    public bool CheckPrereleases { get; set; } = false;
    public DateTime? LastCheckedAt { get; set; }
}

public static class SemVerHelper
{
    public static string CleanVersion(string versionOrTag)
    {
        if (string.IsNullOrWhiteSpace(versionOrTag)) return "0.0.0";
        string clean = versionOrTag.Trim();
        if (clean.StartsWith('v') || clean.StartsWith('V'))
        {
            clean = clean[1..];
        }

        int plusIdx = clean.IndexOf('+');
        if (plusIdx >= 0) clean = clean[..plusIdx];

        int dashIdx = clean.IndexOf('-');
        if (dashIdx >= 0) clean = clean[..dashIdx];

        return clean.Trim();
    }

    public static bool IsNewerVersion(string latestVersionStr, string currentVersionStr)
    {
        string cleanLatest = CleanVersion(latestVersionStr);
        string cleanCurrent = CleanVersion(currentVersionStr);

        if (Version.TryParse(cleanLatest, out var latest) && Version.TryParse(cleanCurrent, out var current))
        {
            return latest > current;
        }

        var latestParts = cleanLatest.Split('.').Select(p => int.TryParse(p, out int v) ? v : 0).ToList();
        var currentParts = cleanCurrent.Split('.').Select(p => int.TryParse(p, out int v) ? v : 0).ToList();

        int maxLen = Math.Max(latestParts.Count, currentParts.Count);
        while (latestParts.Count < maxLen) latestParts.Add(0);
        while (currentParts.Count < maxLen) currentParts.Add(0);

        for (int i = 0; i < maxLen; i++)
        {
            if (latestParts[i] > currentParts[i]) return true;
            if (latestParts[i] < currentParts[i]) return false;
        }

        return false;
    }
}
