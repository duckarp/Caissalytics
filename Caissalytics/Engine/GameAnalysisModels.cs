using Caissalytics.Core;

namespace Caissalytics.Engine;

public enum MoveClassification
{
    Book,
    Best,
    Excellent,
    Good,
    Inaccuracy,
    Mistake,
    Blunder
}

public class PlyAnalysis
{
    public int Ply { get; set; }
    // Set from the actual position so games starting from a Black-to-move FEN are attributed correctly.
    // Falls back to ply parity for reports persisted before these were stored.
    private int? _moveNumber;
    private bool? _isWhiteMove;
    public int MoveNumber { get => _moveNumber ?? (Ply / 2) + 1; set => _moveNumber = value; }
    public bool IsWhiteMove { get => _isWhiteMove ?? Ply % 2 == 0; set => _isWhiteMove = value; }
    public string MoveSan { get; set; } = "";
    public Move Move { get; set; }
    public string FenBefore { get; set; } = "";
    public string FenAfter { get; set; } = "";

    public double? CentipawnsBefore { get; set; }
    public int? MateBefore { get; set; }
    public double? CentipawnsAfter { get; set; }
    public int? MateAfter { get; set; }

    public double WinRateBefore { get; set; }
    public double WinRateAfter { get; set; }
    public double WinRateLoss { get; set; }

    public MoveClassification Classification { get; set; }
    public string? NagGlyph { get; set; }
    public int? NagNumber { get; set; }

    public string BestMoveSan { get; set; } = "";
    public List<string> BestLineMoves { get; set; } = new();
    public string FormattedScoreAfter { get; set; } = "0.00";
}

public class GameAnalysisReport
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<PlyAnalysis> Plies { get; set; } = new();

    /// <summary>
    /// The analyzed line as child indices from the root (Plies[i] is the move reached by the first i+1
    /// steps). Empty for reports saved before this existed: those cover the main line.
    /// </summary>
    public List<int> LinePath { get; set; } = new();

    public double WhiteAccuracy { get; set; }
    public double BlackAccuracy { get; set; }

    public double WhiteAcpl { get; set; }
    public double BlackAcpl { get; set; }

    public int WhiteBestCount { get; set; }
    public int WhiteExcellentCount { get; set; }
    public int WhiteGoodCount { get; set; }
    public int WhiteInaccuracyCount { get; set; }
    public int WhiteMistakeCount { get; set; }
    public int WhiteBlunderCount { get; set; }

    public int BlackBestCount { get; set; }
    public int BlackExcellentCount { get; set; }
    public int BlackGoodCount { get; set; }
    public int BlackInaccuracyCount { get; set; }
    public int BlackMistakeCount { get; set; }
    public int BlackBlunderCount { get; set; }
}

public class GameAnalysisOptions
{
    public string SpeedPreset { get; set; } = "Standard";
    public int MoveTimeMs { get; set; } = 150;
    public int MaxDepth { get; set; } = 16;

    public static GameAnalysisOptions Quick => new() { SpeedPreset = "Quick", MoveTimeMs = 80, MaxDepth = 12 };
    public static GameAnalysisOptions Standard => new() { SpeedPreset = "Standard", MoveTimeMs = 150, MaxDepth = 16 };
    public static GameAnalysisOptions Deep => new() { SpeedPreset = "Deep", MoveTimeMs = 400, MaxDepth = 20 };
}

public class GameAnalysisProgress
{
    public int CurrentPly { get; set; }
    public int TotalPlies { get; set; }
    public int PercentComplete => TotalPlies > 0 ? (int)((CurrentPly / (double)TotalPlies) * 100) : 0;
    public string CurrentMoveSan { get; set; } = "";
    public PlyAnalysis? LatestPly { get; set; }
}
