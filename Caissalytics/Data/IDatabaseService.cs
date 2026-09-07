namespace Caissalytics.Data;

public interface IDatabaseService
{
    event Action? OnActiveDatabaseChanged;
    event Action? OnReferenceDatabaseChanged;
    event Action<string>? OnDatabaseModified;

    Task<List<DatabaseInfo>> GetDatabasesAsync();
    Task<DatabaseInfo> GetActiveDatabaseAsync();
    Task SetActiveDatabaseAsync(string name);
    Task<string> GetReferenceDatabaseAsync();
    Task SetReferenceDatabaseAsync(string name);
    Task<DatabaseInfo> CreateDatabaseAsync(string name);
    Task<bool> DeleteDatabaseAsync(string name);

    Task<List<MasterCatalogItem>> GetMasterCatalogAsync();
    Task InstallMasterDatabaseAsync(string catalogId, IProgress<(int current, int total, string status)>? progress = null, CancellationToken cancellationToken = default);

    Task<PositionReferenceResult> QueryPositionAsync(string? databaseName, ulong zobristKey, int maxGames = 25);
    Task<(List<GameHeader> Games, int TotalCount)> SearchGamesAsync(string? databaseName, GameFilter filter);
    Task<List<GameHeader>> GetAllGameHeadersAsync(string? databaseName = null);
    Task<GameHeader?> GetGameByIdAsync(string? databaseName, long gameId);
    Task<long> SaveGameAsync(string databaseName, GameHeader game);
    Task<bool> DeleteGameAsync(string databaseName, long gameId);

    Task ImportPgnStreamAsync(string databaseName, Stream stream, IProgress<PgnImportProgress>? progress = null, CancellationToken cancellationToken = default, bool deduplicate = false);
    Task ImportPgnStreamAsync(string databaseName, Stream stream, string? fileName, IProgress<PgnImportProgress>? progress = null, CancellationToken cancellationToken = default, bool deduplicate = false)
        => ImportPgnStreamAsync(databaseName, stream, progress, cancellationToken, deduplicate);
    Task ImportPgnTextAsync(string databaseName, string pgnText, IProgress<PgnImportProgress>? progress = null, CancellationToken cancellationToken = default, bool deduplicate = false);
}
