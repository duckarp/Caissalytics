using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Caissalytics.Engine;

public class StockfishUpdateInfo
{
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseTag { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public long AssetSizeBytes { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public bool IsChecking { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public DateTime? CheckedAt { get; set; }

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

public record StockfishReleaseAsset(string Name, string Url, long Size);

public static class StockfishVersionHelper
{
    public static double ParseStockfishVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var match = Regex.Match(text, @"(?:sf_?|stockfish\s*|v)?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ver))
        {
            return ver;
        }
        return 0;
    }

    public static StockfishReleaseAsset? SelectBestAsset(
        IEnumerable<StockfishReleaseAsset> assets,
        OSPlatform? overrideOs = null,
        Architecture? overrideArch = null)
    {
        bool isWindows = overrideOs.HasValue ? overrideOs.Value == OSPlatform.Windows : RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool isMac = overrideOs.HasValue ? overrideOs.Value == OSPlatform.OSX : RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        bool isLinux = overrideOs.HasValue ? overrideOs.Value == OSPlatform.Linux : RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        var arch = overrideArch ?? RuntimeInformation.ProcessArchitecture;

        var assetList = assets.ToList();
        if (assetList.Count == 0) return null;

        if (isWindows)
        {
            if (arch == Architecture.Arm64)
            {
                var arm = assetList.FirstOrDefault(a => a.Name.Contains("windows", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase));
                if (arm != null) return arm;
            }
            return assetList.FirstOrDefault(a => a.Name.Contains("windows", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("x86-64-universal", StringComparison.OrdinalIgnoreCase))
                ?? assetList.FirstOrDefault(a => a.Name.Contains("windows", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("avx2", StringComparison.OrdinalIgnoreCase))
                ?? assetList.FirstOrDefault(a => a.Name.Contains("windows", StringComparison.OrdinalIgnoreCase) && (a.Name.Contains("x86-64", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase)))
                ?? assetList.FirstOrDefault(a => a.Name.Contains("windows", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

        if (isMac)
        {
            return assetList.FirstOrDefault(a => a.Name.Contains("macos", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("universal", StringComparison.OrdinalIgnoreCase))
                ?? assetList.FirstOrDefault(a => a.Name.Contains("macos", StringComparison.OrdinalIgnoreCase) && (a.Name.Contains("apple-silicon", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("m1", StringComparison.OrdinalIgnoreCase)))
                ?? assetList.FirstOrDefault(a => a.Name.Contains("macos", StringComparison.OrdinalIgnoreCase));
        }

        if (isLinux)
        {
            if (arch == Architecture.Arm64)
            {
                var arm = assetList.FirstOrDefault(a => (a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("ubuntu", StringComparison.OrdinalIgnoreCase)) && a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase));
                if (arm != null) return arm;
            }
            return assetList.FirstOrDefault(a => a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("x86-64-universal", StringComparison.OrdinalIgnoreCase))
                ?? assetList.FirstOrDefault(a => (a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("ubuntu", StringComparison.OrdinalIgnoreCase)) && a.Name.Contains("avx2", StringComparison.OrdinalIgnoreCase))
                ?? assetList.FirstOrDefault(a => (a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("ubuntu", StringComparison.OrdinalIgnoreCase)) && (a.Name.Contains("x86-64", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase)))
                ?? assetList.FirstOrDefault(a => a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("ubuntu", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }
}

public class EngineInfo
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? ExecutablePath { get; set; }
    public bool IsInstalled { get; set; }
    public string? DownloadUrl { get; set; }
    public string Version { get; set; } = "1.0";
    public string Author { get; set; } = "";
    public bool IsCustom { get; set; }
    public bool IsActive { get; set; }
    public DateTime? DateAdded { get; set; }
}

public class EngineProbeResult
{
    public bool Success { get; set; }
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "";
    public string? ErrorMessage { get; set; }
}

public class EnginesConfigFile
{
    public string ActiveEngineId { get; set; } = "stockfish-17";
    public string? SyzygyPath { get; set; }
    public List<EngineInfo> Engines { get; set; } = new();
}

public class EngineEvaluationLine
{
    public int MultiPvIndex { get; set; } = 1;
    public int Depth { get; set; }
    public int SelectiveDepth { get; set; }
    public double? Centipawns { get; set; }
    public int? MateInMoves { get; set; }
    public long Nodes { get; set; }
    public long Nps { get; set; }
    public List<string> PvMoves { get; set; } = new();

    public string BestMove => PvMoves.Count > 0 ? PvMoves[0] : "";

    public string FormattedScore
    {
        get
        {
            if (MateInMoves.HasValue)
            {
                return MateInMoves.Value > 0 ? $"M{MateInMoves.Value}" : $"-M{Math.Abs(MateInMoves.Value)}";
            }
            if (Centipawns.HasValue)
            {
                double cp = Centipawns.Value / 100.0;
                return cp >= 0 ? $"+{cp:F2}" : $"{cp:F2}";
            }
            return "0.00";
        }
    }

    public double WhiteWinPercentage
    {
        get
        {
            if (MateInMoves.HasValue)
            {
                return MateInMoves.Value > 0 ? 100.0 : 0.0;
            }
            if (Centipawns.HasValue)
            {
                // Standard Lichess winning chances formula
                double cp = Centipawns.Value;
                return 50.0 + 50.0 * (2.0 / (1.0 + Math.Exp(-0.00368208 * cp)) - 1.0);
            }
            return 50.0;
        }
    }
}
