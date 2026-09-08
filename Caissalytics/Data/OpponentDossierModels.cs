namespace Caissalytics.Data;

public class OpponentScoutingReport
{
    public string PlayerName { get; set; } = string.Empty;
    public string DatabaseSource { get; set; } = string.Empty;
    public int TotalGames { get; set; }
    public int WhiteGamesCount { get; set; }
    public int BlackGamesCount { get; set; }

    public int TotalWins { get; set; }
    public int TotalDraws { get; set; }
    public int TotalLosses { get; set; }
    public double OverallScore => TotalGames > 0 ? Math.Round((TotalWins + 0.5 * TotalDraws) * 100.0 / TotalGames, 1) : 0;
    public double OverallWinRate => TotalGames > 0 ? Math.Round(TotalWins * 100.0 / TotalGames, 1) : 0;

    public int WhiteWins { get; set; }
    public int WhiteDraws { get; set; }
    public int WhiteLosses { get; set; }
    public double WhiteScore => WhiteGamesCount > 0 ? Math.Round((WhiteWins + 0.5 * WhiteDraws) * 100.0 / WhiteGamesCount, 1) : 0;

    public int BlackWins { get; set; }
    public int BlackDraws { get; set; }
    public int BlackLosses { get; set; }
    public double BlackScore => BlackGamesCount > 0 ? Math.Round((BlackWins + 0.5 * BlackDraws) * 100.0 / BlackGamesCount, 1) : 0;

    public int? PeakElo { get; set; }
    public int? CurrentElo { get; set; }
    public int? AvgElo { get; set; }
    public string? EarliestDate { get; set; }
    public string? LatestDate { get; set; }

    public string PlayingStyle { get; set; } = "Balanced";
    public string StyleDescription { get; set; } = string.Empty;

    public List<OpponentRepertoireBranch> WhiteRepertoire { get; set; } = new();
    public List<OpponentRepertoireBranch> BlackRepertoire { get; set; } = new();
    public GameLengthTendencies LengthTendencies { get; set; } = new();
    public List<OpponentVulnerability> Vulnerabilities { get; set; } = new();
    public List<GameHeader> RecentGames { get; set; } = new();

    // Internet Scouting (FIDE & Chess-Results)
    public FidePlayerCard? FideCard { get; set; }
    public List<ChessResultsTournamentEntry> RecentTournaments { get; set; } = new();
    public string? ChessResultsUrl { get; set; }
}

public class OpponentRepertoireBranch
{
    public string Move { get; set; } = string.Empty;
    public string Category { get; set; } = "Main Line";
    public int GameCount { get; set; }
    public double FrequencyPct { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public double ScorePct => GameCount > 0 ? Math.Round((Wins + 0.5 * Draws) * 100.0 / GameCount, 1) : 0;
    public List<OpponentOpeningLine> KeyLines { get; set; } = new();
}

public class OpponentOpeningLine
{
    public string Eco { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MoveSequence { get; set; } = string.Empty;
    public int GameCount { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public double ScorePct => GameCount > 0 ? Math.Round((Wins + 0.5 * Draws) * 100.0 / GameCount, 1) : 0;
    public long? SampleGameId { get; set; }
}

public class OpponentVulnerability
{
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = "Moderate"; // "High", "Moderate", "Noticeable"
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

public class GameLengthTendencies
{
    public int ShortGamesCount { get; set; } // < 30 moves
    public int ShortGamesWins { get; set; }
    public int ShortGamesDraws { get; set; }
    public double ShortGamesScore => ShortGamesCount > 0 ? Math.Round((ShortGamesWins + 0.5 * ShortGamesDraws) * 100.0 / ShortGamesCount, 1) : 0;

    public int MediumGamesCount { get; set; } // 30-49 moves
    public int MediumGamesWins { get; set; }
    public int MediumGamesDraws { get; set; }
    public double MediumGamesScore => MediumGamesCount > 0 ? Math.Round((MediumGamesWins + 0.5 * MediumGamesDraws) * 100.0 / MediumGamesCount, 1) : 0;

    public int LongGamesCount { get; set; } // 50+ moves
    public int LongGamesWins { get; set; }
    public int LongGamesDraws { get; set; }
    public double LongGamesScore => LongGamesCount > 0 ? Math.Round((LongGamesWins + 0.5 * LongGamesDraws) * 100.0 / LongGamesCount, 1) : 0;
}

public class FidePlayerCard
{
    public string FideId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string TitleAbbreviation => Title switch
    {
        "Grandmaster" => "GM",
        "International Master" => "IM",
        "FIDE Master" => "FM",
        "Candidate Master" => "CM",
        "Woman Grandmaster" => "WGM",
        "Woman International Master" => "WIM",
        "Woman FIDE Master" => "WFM",
        "Woman Candidate Master" => "WCM",
        _ => Title ?? string.Empty
    };
    public string? Federation { get; set; }
    public string? FederationCode { get; set; }
    public string? FlagUrl { get; set; }
    public int? BirthYear { get; set; }
    public string? Gender { get; set; }
    public int? StandardElo { get; set; }
    public int? RapidElo { get; set; }
    public int? BlitzElo { get; set; }
    public int? WorldRankActive { get; set; }
    public int? WorldRankAll { get; set; }
    public int? NationalRank { get; set; }
    public string ProfileUrl => $"https://ratings.fide.com/profile/{FideId}";
}

public class FideSearchResult
{
    public string FideId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Federation { get; set; } = string.Empty;
    public int? StandardElo { get; set; }
    public int? RapidElo { get; set; }
    public int? BlitzElo { get; set; }
    public int? BirthYear { get; set; }
}

public class ChessResultsTournamentEntry
{
    public string TournamentName { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string ScoreOrRank { get; set; } = string.Empty;
    public string Rounds { get; set; } = string.Empty;
    public string TotalPlayers { get; set; } = string.Empty;
    public string Club { get; set; } = string.Empty;
    public string Federation { get; set; } = string.Empty;
    public string TournamentUrl { get; set; } = string.Empty;
    public string PlayerCardUrl { get; set; } = string.Empty;
}
