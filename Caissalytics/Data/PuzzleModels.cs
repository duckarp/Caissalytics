using System.Text.Json.Serialization;

namespace Caissalytics.Data;

public class ChessPuzzle
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = "Curated Master Tactic";
    public long? GameId { get; set; }
    public string? DatabaseName { get; set; }
    public string WhitePlayer { get; set; } = "White";
    public string BlackPlayer { get; set; } = "Black";
    public string? Date { get; set; }
    public string? Opening { get; set; }
    public string? Event { get; set; }
    public string Fen { get; set; } = string.Empty;
    public string PlayerColor { get; set; } = "white"; // "white" or "black"
    public string? OpponentMoveLeadingIn { get; set; } // e.g. "17... g5"
    public string? PlayedBlunderSan { get; set; } // e.g. "Nxd4??"
    public List<string> SolutionMovesSan { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
    public int Rating { get; set; } = 1500;
    public List<string> Themes { get; set; } = new();
    public bool IsUserBlunder { get; set; }
}

public class PuzzleAttempt
{
    public string PuzzleId { get; set; } = string.Empty;
    public DateTime SolvedAt { get; set; } = DateTime.UtcNow;
    public bool IsSuccess { get; set; }
    public int TimeSpentSeconds { get; set; }
    public int RatingDelta { get; set; }
    public int NewRating { get; set; }
}

public class PuzzleStats
{
    public int CurrentRating { get; set; } = 1500;
    public int SolvedCount { get; set; }
    public int FailedCount { get; set; }
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }

    public HashSet<string> SolvedPuzzleIds { get; set; } = new();
    public HashSet<string> FailedPuzzleIds { get; set; } = new();
    public List<PuzzleAttempt> History { get; set; } = new();

    [JsonIgnore]
    public int TotalAttempts => SolvedCount + FailedCount;

    [JsonIgnore]
    public double WinRate => TotalAttempts > 0 ? (SolvedCount * 100.0) / TotalAttempts : 0.0;
}

public class PuzzleFilterOptions
{
    public string Mode { get; set; } = "all"; // "all", "blunders", "curated", "review"
    public string? MinRating { get; set; }
    public string? MaxRating { get; set; }
}
