namespace Caissalytics.Data;

public interface IDatabaseService
{
    public const string ProtectedOnlineDatabaseName = "My online games";

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
    Task<bool> RenameDatabaseAsync(string oldName, string newName) => Task.FromResult(false);

    Task<List<MasterCatalogItem>> GetMasterCatalogAsync();
    Task InstallMasterDatabaseAsync(string catalogId, IProgress<(int current, int total, string status)>? progress = null, CancellationToken cancellationToken = default);

    Task<PositionReferenceResult> QueryPositionAsync(string? databaseName, ulong zobristKey, int maxGames = 25);
    Task<(List<GameHeader> Games, int TotalCount)> SearchGamesAsync(string? databaseName, GameFilter filter);
    Task<List<GameHeader>> GetAllGameHeadersAsync(string? databaseName = null);
    Task<GameHeader?> GetGameByIdAsync(string? databaseName, long gameId);
    Task<long> SaveGameAsync(string databaseName, GameHeader game);
    Task<bool> DeleteGameAsync(string databaseName, long gameId);
    Task UpdateGameNoteAsync(string databaseName, long gameId, string note) => Task.CompletedTask;
    Task ReorderGamesAsync(string databaseName, IReadOnlyList<long> orderedGameIds) => Task.CompletedTask;

    Task<List<GameFolder>> GetFoldersAsync(string databaseName) => Task.FromResult(new List<GameFolder>());
    Task<GameFolder> CreateFolderAsync(string databaseName, string name, long? parentFolderId = null) => Task.FromResult(new GameFolder { Name = name, ParentId = parentFolderId });
    Task<bool> RenameFolderAsync(string databaseName, long folderId, string newName) => Task.FromResult(false);
    Task<bool> MoveFolderAsync(string databaseName, long folderId, long? targetParentFolderId) => Task.FromResult(false);
    Task<bool> DeleteFolderAsync(string databaseName, long folderId) => Task.FromResult(false);
    Task MoveGameToFolderAsync(string databaseName, long gameId, long? folderId) => Task.CompletedTask;
    Task MoveGamesAsync(string sourceDbName, IReadOnlyList<long> gameIds, string targetDbName, long? targetFolderId) => Task.CompletedTask;
    Task BulkUpdateGamesMetadataAsync(string databaseName, IReadOnlyList<long> gameIds, BulkGameMetadataUpdate update) => Task.CompletedTask;

    Task ImportPgnStreamAsync(string databaseName, Stream stream, IProgress<PgnImportProgress>? progress = null, CancellationToken cancellationToken = default, bool deduplicate = false, bool allowProtectedDatabase = false);
    Task ImportPgnStreamAsync(string databaseName, Stream stream, string? fileName, IProgress<PgnImportProgress>? progress = null, CancellationToken cancellationToken = default, bool deduplicate = false, bool allowProtectedDatabase = false)
        => ImportPgnStreamAsync(databaseName, stream, progress, cancellationToken, deduplicate, allowProtectedDatabase);
    Task ImportPgnTextAsync(string databaseName, string pgnText, IProgress<PgnImportProgress>? progress = null, CancellationToken cancellationToken = default, bool deduplicate = false, bool allowProtectedDatabase = false);

    /// <summary>
    /// Classifies games whose ECO tag is missing (empty, NULL or "???") from their moves
    /// and stores the result. Returns the number of games updated.
    /// </summary>
    Task<int> FillMissingEcoAsync(string databaseName, IProgress<(int current, int total, string status)>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the PGN of the matching games in date-descending order in batches via <c>pgnChunk</c>.
    /// Pass a null filter to export every game in the database.
    /// Returns the number of games exported.
    /// </summary>
    Task<int> ExportGamesToPgnAsync(string databaseName, GameFilter? filter, Func<string, Task> pgnChunk, IProgress<(int current, int total, string status)>? progress = null, CancellationToken cancellationToken = default);
}
