namespace Caissalytics.Data;

public interface IOpponentDossierService
{
    Task<OpponentScoutingReport?> GenerateDossierAsync(string playerName, string? databaseName = null, CancellationToken cancellationToken = default);
    Task<List<string>> SearchKnownPlayersAsync(string query, string? databaseName = null, int limit = 15);
    Task<OnlineSyncResult> FetchOnlineOpponentGamesAsync(string platform, string username, int maxGames = 50, string? targetDatabase = null, CancellationToken cancellationToken = default);
    Task<FidePlayerCard?> LookupFidePlayerAsync(string fideIdOrName, CancellationToken cancellationToken = default);
    Task<List<FideSearchResult>> SearchFidePlayersAsync(string query, CancellationToken cancellationToken = default);
    Task<List<ChessResultsTournamentEntry>> FetchTournamentsAsync(string? fideId, string? lastName, CancellationToken cancellationToken = default);
    Task<OnlineSyncResult> FetchChessResultsGamesAsync(string? fideId, string? lastName, string? targetDatabase = null, CancellationToken cancellationToken = default);
}
