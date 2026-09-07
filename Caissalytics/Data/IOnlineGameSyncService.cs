namespace Caissalytics.Data;

public interface IOnlineGameSyncService
{
    string OnlineGamesDatabaseName { get; }
    Task<OnlineSyncConfig> GetConfigAsync();
    Task SaveConfigAsync(OnlineSyncConfig config);
    Task<OnlineSyncResult> SyncGamesAsync(
        OnlineSyncConfig config,
        IProgress<OnlineSyncProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
