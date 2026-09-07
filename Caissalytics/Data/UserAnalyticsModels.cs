using Caissalytics.Core;

namespace Caissalytics.Data;

public class AnalyticsFilterOptions
{
    public string TimeFilter { get; set; } = "all"; // "all", "30d", "90d", "year"
    public string ColorFilter { get; set; } = "all"; // "all", "white", "black"
    public string PlatformFilter { get; set; } = "all"; // "all", "lichess", "chesscom", "otb"
}

public class ColorPerformance
{
    public int TotalGames { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }

    public double WinRate => TotalGames > 0 ? (Wins * 100.0 / TotalGames) : 0;
    public double DrawRate => TotalGames > 0 ? (Draws * 100.0 / TotalGames) : 0;
    public double LossRate => TotalGames > 0 ? (Losses * 100.0 / TotalGames) : 0;
    public double ScoreRate => TotalGames > 0 ? ((Wins + 0.5 * Draws) * 100.0 / TotalGames) : 0;

    public int WinPct => TotalGames > 0 ? Math.Clamp((int)Math.Round(Wins * 100.0 / TotalGames), 0, 100) : 0;
    public int DrawPct => TotalGames > 0 ? Math.Clamp((int)Math.Round(Draws * 100.0 / TotalGames), 0, 100) : 0;
    public int LossPct => TotalGames > 0 ? Math.Clamp(100 - WinPct - DrawPct, 0, 100) : 0;

    public int? AverageUserRating { get; set; }
    public int? AverageOpponentRating { get; set; }
}

public class RatingHistoryPoint
{
    public long GameId { get; set; }
    public DateTime? Date { get; set; }
    public string DateStr { get; set; } = "";
    public int Rating { get; set; }
    public int? OpponentRating { get; set; }
    public string OpponentName { get; set; } = "";
    public string UserColor { get; set; } = "white";
    public string Result { get; set; } = "draw"; // "win", "draw", "loss"
    public string Platform { get; set; } = "";
    public string PlatformName => Platform switch
    {
        "lichess" => "Lichess",
        "chesscom" => "Chess.com",
        _ => "OTB / Local"
    };
    public string Eco { get; set; } = "";
    public string OpeningName { get; set; } = "";
}

public class PlatformRatingOverview
{
    public string PlatformId { get; set; } = ""; // "lichess", "chesscom", "otb"
    public string PlatformName { get; set; } = ""; // "Lichess", "Chess.com", "OTB / Local"
    public int? CurrentRating { get; set; }
    public int? PeakRating { get; set; }
    public int? LowestRating { get; set; }
    public List<RatingHistoryPoint> Points { get; set; } = new();
}

public class OpeningPerformanceStat
{
    public string Eco { get; set; } = "";
    public string OpeningName { get; set; } = "";
    public int TotalGames { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }

    public double WinRate => TotalGames > 0 ? (Wins * 100.0 / TotalGames) : 0;
    public double DrawRate => TotalGames > 0 ? (Draws * 100.0 / TotalGames) : 0;
    public double LossRate => TotalGames > 0 ? (Losses * 100.0 / TotalGames) : 0;
    public double ScoreRate => TotalGames > 0 ? ((Wins + 0.5 * Draws) * 100.0 / TotalGames) : 0;

    public int WinPct => TotalGames > 0 ? Math.Clamp((int)Math.Round(Wins * 100.0 / TotalGames), 0, 100) : 0;
    public int DrawPct => TotalGames > 0 ? Math.Clamp((int)Math.Round(Draws * 100.0 / TotalGames), 0, 100) : 0;
    public int LossPct => TotalGames > 0 ? Math.Clamp(100 - WinPct - DrawPct, 0, 100) : 0;

    public string Color { get; set; } = "Both"; // "White", "Black", "Both"
}

public class PersonalGameSummary
{
    public long GameId { get; set; }
    public string Date { get; set; } = "";
    public string FormattedDate => DateHelper.Format(Date);
    public string UserColor { get; set; } = "white"; // "white", "black"
    public int? UserRating { get; set; }
    public int? OpponentRating { get; set; }
    public string OpponentName { get; set; } = "";
    public string Result { get; set; } = "draw"; // "win", "draw", "loss"
    public string ResultDisplay => Result switch
    {
        "win" => "Win",
        "loss" => "Loss",
        _ => "Draw"
    };
    public string Eco { get; set; } = "";
    public string OpeningName { get; set; } = "";
    public string Platform { get; set; } = "";
    public string PlatformName => Platform switch
    {
        "lichess" => "Lichess",
        "chesscom" => "Chess.com",
        _ => "OTB / Local"
    };
    public int PlyCount { get; set; }
    public GameHeader Header { get; set; } = default!;
}

public class PersonalAnalyticsReport
{
    public UserProfile Profile { get; set; } = new();
    public string DatabaseScope { get; set; } = "";
    public int TotalGames { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }

    public double WinRate => TotalGames > 0 ? (Wins * 100.0 / TotalGames) : 0;
    public double DrawRate => TotalGames > 0 ? (Draws * 100.0 / TotalGames) : 0;
    public double LossRate => TotalGames > 0 ? (Losses * 100.0 / TotalGames) : 0;
    public double ScoreRate => TotalGames > 0 ? ((Wins + 0.5 * Draws) * 100.0 / TotalGames) : 0;

    public int WinPct => TotalGames > 0 ? Math.Clamp((int)Math.Round(Wins * 100.0 / TotalGames), 0, 100) : 0;
    public int DrawPct => TotalGames > 0 ? Math.Clamp((int)Math.Round(Draws * 100.0 / TotalGames), 0, 100) : 0;
    public int LossPct => TotalGames > 0 ? Math.Clamp(100 - WinPct - DrawPct, 0, 100) : 0;

    public string CurrentStreak { get; set; } = "0";
    public int BestWinStreak { get; set; }

    public int? CurrentRating { get; set; }
    public int? PeakRating { get; set; }
    public int? LowestRating { get; set; }
    public int? AverageUserRating { get; set; }
    public int? AverageOpponentRating { get; set; }

    public ColorPerformance WhiteStats { get; set; } = new();
    public ColorPerformance BlackStats { get; set; } = new();

    public List<RatingHistoryPoint> RatingHistory { get; set; } = new();
    public List<PlatformRatingOverview> PlatformRatings { get; set; } = new();
    public List<OpeningPerformanceStat> TopOpenings { get; set; } = new();
    public List<PersonalGameSummary> RecentGames { get; set; } = new();

    public int PlatformLichessGames { get; set; }
    public int PlatformChessComGames { get; set; }
    public int PlatformOtherGames { get; set; }
}
