namespace Caissalytics.Data;

public interface IOpponentDossierService
{
    Task<OpponentScoutingReport?> GenerateDossierAsync(string playerName, string? databaseName = null, CancellationToken cancellationToken = default);
    Task<List<string>> SearchKnownPlayersAsync(string query, string? databaseName = null, int limit = 15);
    Task<OnlineSyncResult> FetchOnlineOpponentGamesAsync(string platform, string username, int maxGames = 50, CancellationToken cancellationToken = default);
}
