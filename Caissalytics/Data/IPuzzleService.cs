namespace Caissalytics.Data;

public interface IPuzzleService
{
    event Action? OnPuzzleStatsChanged;

    Task<PuzzleStats> GetStatsAsync();
    Task SaveStatsAsync(PuzzleStats stats);

    Task<List<ChessPuzzle>> GetPuzzlesAsync(PuzzleFilterOptions filter, CancellationToken ct = default);
    Task<int> ExtractPuzzlesFromDatabaseAsync(string databaseName, CancellationToken ct = default);
    Task<PuzzleStats> RecordAttemptAsync(string puzzleId, bool isSuccess, int timeSpentSeconds);
    List<ChessPuzzle> GetCuratedPuzzles();
}
