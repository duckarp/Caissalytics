namespace Caissalytics.Data;

public interface IUpdateService
{
    event Action<UpdateInfo>? OnUpdateStateChanged;

    UpdateInfo? CachedUpdate { get; }
    string GetCurrentVersion();

    Task<UpdateInfo> CheckForUpdatesAsync(bool force = false, CancellationToken ct = default);
    Task DownloadUpdateAsync(UpdateInfo update, IProgress<(int current, int total, string status)>? progress = null, CancellationToken ct = default);
    Task ApplyUpdateAndRestartAsync();

    Task<UpdateSettings> GetSettingsAsync();
    Task SaveSettingsAsync(UpdateSettings settings);
}
