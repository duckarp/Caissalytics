using System.Text.Json.Serialization;

namespace Caissalytics.Core;

public enum TablebaseCategory
{
    Unknown,
    Win,
    Draw,
    Loss,
    BlessedLoss,
    CursedWin,
    Checkmate,
    Stalemate
}

public class TablebaseMove
{
    public string Uci { get; set; } = string.Empty;
    public string San { get; set; } = string.Empty;
    public TablebaseCategory Category { get; set; } = TablebaseCategory.Unknown;
    public int? Dtz { get; set; }
    public int? Dtm { get; set; }
    public bool IsZeroing { get; set; }
    public bool IsCheckmate { get; set; }
    public bool IsStalemate { get; set; }
    public bool IsConversion { get; set; }

    public string FormattedDtz => Dtz.HasValue && Dtz.Value != 0 ? $"DTZ {Math.Abs(Dtz.Value)}" : string.Empty;
    public string FormattedDtm => Dtm.HasValue && Dtm.Value != 0 ? $"M{Math.Abs(Dtm.Value)}" : string.Empty;
}

public class TablebaseResult
{
    public string Fen { get; set; } = string.Empty;
    public TablebaseCategory Category { get; set; } = TablebaseCategory.Unknown;
    public int? Dtz { get; set; }
    public int? Dtm { get; set; }
    public bool Checkmate { get; set; }
    public bool Stalemate { get; set; }
    public bool InsufficientMaterial { get; set; }
    public List<TablebaseMove> Moves { get; set; } = new();
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }

    public string FormattedVerdict
    {
        get
        {
            if (Checkmate) return "Checkmate";
            if (Stalemate) return "Stalemate (Draw)";
            if (InsufficientMaterial) return "Draw (Insufficient Material)";

            string distance = string.Empty;
            if (Dtm.HasValue && Dtm.Value != 0)
                distance = $" (Mate in {Math.Abs(Dtm.Value)})";
            else if (Dtz.HasValue && Dtz.Value != 0)
                distance = $" (DTZ {Math.Abs(Dtz.Value)})";

            return Category switch
            {
                TablebaseCategory.Win => $"Winning{distance}",
                TablebaseCategory.CursedWin => $"Cursed Win (50-move rule draw){distance}",
                TablebaseCategory.Draw => "Theoretical Draw",
                TablebaseCategory.BlessedLoss => $"Blessed Loss (50-move rule draw){distance}",
                TablebaseCategory.Loss => $"Losing{distance}",
                _ => "Unknown Tablebase Position"
            };
        }
    }
}

public enum EndgameCategory
{
    Pawns,
    Rooks,
    Queens,
    MinorPieces,
    Practical
}

public enum EndgameDifficulty
{
    Beginner,
    Intermediate,
    Advanced,
    Master
}

public class EndgamePosition
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public EndgameCategory Category { get; set; }
    public EndgameDifficulty Difficulty { get; set; }
    public string Fen { get; set; } = string.Empty;
    public PieceColor PlayerColor { get; set; } = PieceColor.White;
    public string TargetOutcome { get; set; } = "Win"; // "Win" or "Draw"
    public string Description { get; set; } = string.Empty;
    public string CoachingTip { get; set; } = string.Empty;
    public List<string> KeySquares { get; set; } = new();
    public string BenchmarkMoves { get; set; } = string.Empty;
}
