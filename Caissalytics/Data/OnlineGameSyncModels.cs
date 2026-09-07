namespace Caissalytics.Data;

public class OnlineSyncConfig
{
    public string LichessUsername { get; set; } = "";
    public string ChessComUsername { get; set; } = "";
    public int MaxGamesPerPlatform { get; set; } = 100;
    public DateTime? LastSyncUtc { get; set; }
}

public class OnlineSyncProgress
{
    public string Platform { get; set; } = "";
    public string CurrentStage { get; set; } = "Initializing";
    public int GamesDownloaded { get; set; }
    public int GamesImported { get; set; }
    public int GamesSkipped { get; set; }
    public int PercentComplete { get; set; }
    public bool IsFinished { get; set; }
    public string? ErrorMessage { get; set; }
}

public class OnlineSyncResult
{
    public bool Success => Errors.Count == 0 || TotalImported > 0;
    public int TotalLichessGames { get; set; }
    public int TotalChessComGames { get; set; }
    public int TotalImported { get; set; }
    public int TotalSkipped { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Errors { get; set; } = new();
}
