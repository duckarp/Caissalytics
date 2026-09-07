using Caissalytics.Core;

namespace Caissalytics.Data;

public class DatabaseInfo
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int GameCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; }

    public string FormattedSize
    {
        get
        {
            if (SizeBytes < 1024) return $"{SizeBytes} B";
            if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:F1} KB";
            if (SizeBytes < 1024 * 1024 * 1024) return $"{SizeBytes / (1024.0 * 1024.0):F1} MB";
            return $"{SizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }
}

public class GameHeader
{
    public long Id { get; set; }
    public string White { get; set; } = "White";
    public string Black { get; set; } = "Black";
    public int? WhiteElo { get; set; }
    public int? BlackElo { get; set; }
    public string Result { get; set; } = "*";
    public string Date { get; set; } = "????.??.??";
    public string FormattedDate => DateHelper.Format(Date);
    public string Event { get; set; } = string.Empty;
    public string Site { get; set; } = string.Empty;
    public string Round { get; set; } = string.Empty;
    public string Eco { get; set; } = string.Empty;
    public int PlyCount { get; set; }
    public string Pgn { get; set; } = string.Empty;

    public string Platform
    {
        get
        {
            if (Site.Contains("lichess.org", StringComparison.OrdinalIgnoreCase) ||
                Event.Contains("lichess", StringComparison.OrdinalIgnoreCase) ||
                Site.StartsWith("lichess", StringComparison.OrdinalIgnoreCase))
            {
                return "lichess";
            }

            if (Site.Contains("chess.com", StringComparison.OrdinalIgnoreCase) ||
                Site.Equals("Chess.com", StringComparison.OrdinalIgnoreCase) ||
                Event.Contains("chess.com", StringComparison.OrdinalIgnoreCase) ||
                Site.StartsWith("chess.com", StringComparison.OrdinalIgnoreCase))
            {
                return "chesscom";
            }

            if (!string.IsNullOrEmpty(Pgn))
            {
                if (Pgn.Contains("lichess.org", StringComparison.OrdinalIgnoreCase))
                    return "lichess";
                if (Pgn.Contains("chess.com", StringComparison.OrdinalIgnoreCase))
                    return "chesscom";
            }

            return "";
        }
    }

    public string PlatformName => Platform switch
    {
        "lichess" => "Lichess",
        "chesscom" => "Chess.com",
        _ => ""
    };

    public string? ExternalUrl
    {
        get
        {
            if (Site.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                Site.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return Site;
            }
            return null;
        }
    }
}

public class PositionMoveStat
{
    public string MoveSan { get; set; } = string.Empty;
    public string MoveUci { get; set; } = string.Empty;
    public int TotalGames { get; set; }
    public int WhiteWins { get; set; }
    public int Draws { get; set; }
    public int BlackWins { get; set; }

    public double WhiteWinRate => TotalGames > 0 ? (WhiteWins * 100.0 / TotalGames) : 0;
    public double DrawRate => TotalGames > 0 ? (Draws * 100.0 / TotalGames) : 0;
    public double BlackWinRate => TotalGames > 0 ? (BlackWins * 100.0 / TotalGames) : 0;
    public double ScoreRate => TotalGames > 0 ? ((WhiteWins + 0.5 * Draws) * 100.0 / TotalGames) : 0;

    public int WhiteWinPct => TotalGames > 0 ? Math.Clamp((int)Math.Round(WhiteWins * 100.0 / TotalGames), 0, 100) : 0;
    public int DrawPct => TotalGames > 0 ? Math.Clamp((int)Math.Round(Draws * 100.0 / TotalGames), 0, 100) : 0;
    public int BlackWinPct => TotalGames > 0 ? Math.Clamp((int)Math.Round(BlackWins * 100.0 / TotalGames), 0, 100) : 0;
}

public class PositionReferenceResult
{
    public ulong ZobristKey { get; set; }
    public int TotalPositionGames { get; set; }
    public List<PositionMoveStat> CandidateMoves { get; set; } = new();
    public List<GameHeader> TopGames { get; set; } = new();
}

public class GameFilter
{
    public string? Player { get; set; }
    public string? Eco { get; set; }
    public int? MinElo { get; set; }
    public int? MaxElo { get; set; }
    public string? Result { get; set; }
    public string? Event { get; set; }
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class PgnImportProgress
{
    public int GamesParsed { get; set; }
    public int GamesSaved { get; set; }
    public int GamesSkipped { get; set; }
    public long BytesProcessed { get; set; }
    public long TotalBytes { get; set; }
    public string CurrentStage { get; set; } = "Initializing";
    public TimeSpan Elapsed { get; set; }

    public int PercentComplete => TotalBytes > 0 ? Math.Clamp((int)(BytesProcessed * 100 / TotalBytes), 0, 100) : 0;
    public double GamesPerSecond => Elapsed.TotalSeconds > 0 ? GamesSaved / Elapsed.TotalSeconds : 0;
}
