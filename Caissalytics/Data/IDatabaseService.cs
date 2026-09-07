namespace Caissalytics.Data;

public interface IDatabaseService
{
    event Action? OnActiveDatabaseChanged;
    event Action<string>? OnDatabaseModified;

    Task<List<DatabaseInfo>> GetDatabasesAsync();
    Task<DatabaseInfo> GetActiveDatabaseAsync();
    Task SetActiveDatabaseAsync(string name);
    Task<DatabaseInfo> CreateDatabaseAsync(string name);
    Task<bool> DeleteDatabaseAsync(string name);

    Task<PositionReferenceResult> QueryPositionAsync(string? databaseName, ulong zobristKey, int maxGames = 25);
    Task<(List<GameHeader> Games, int TotalCount)> SearchGamesAsync(string? databaseName, GameFilter filter);
    Task<GameHeader?> GetGameByIdAsync(string? databaseName, long gameId);
    Task<long> SaveGameAsync(string databaseName, GameHeader game);
    Task<bool> DeleteGameAsync(string databaseName, long gameId);

    Task ImportPgnStreamAsync(string databaseName, Stream stream, IProgress<PgnImportProgress>? progress = null, CancellationToken cancellationToken = default);
    Task ImportPgnTextAsync(string databaseName, string pgnText, IProgress<PgnImportProgress>? progress = null, CancellationToken cancellationToken = default);
}
