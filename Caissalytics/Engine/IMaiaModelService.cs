namespace Caissalytics.Engine;

public record MaiaModelInfo
{
    public int Rating { get; init; }
    public string Name { get; init; } = "";
    public string WeightsFileName { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string? FilePath { get; set; }
    public long FileSizeBytes { get; set; }
    public bool IsDownloaded => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
}

public interface IMaiaModelService
{
    IReadOnlyList<MaiaModelInfo> Models { get; }
    string ModelsDirectory { get; }
    string? CustomLc0Path { get; set; }
    string? FindLc0Executable();
    bool IsLc0Available { get; }
    MaiaModelInfo? GetModel(int rating);
    MaiaModelInfo GetClosestModel(int rating);
    Task<bool> DownloadModelAsync(int rating, IProgress<int>? progress = null, CancellationToken ct = default);
    Task<bool> DownloadAllModelsAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    Task<bool> EnsureWindowsLc0Async(IProgress<int>? progress = null, CancellationToken ct = default);
    Task<bool> EnsureLc0Async(IProgress<(string phase, int pct)>? progress = null, CancellationToken ct = default);
    string? LastInstallError { get; }
    void SaveConfig();
    event Action? OnModelsChanged;
}
