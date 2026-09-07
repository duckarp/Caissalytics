namespace Caissalytics.Engine;

public class EngineInfo
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? ExecutablePath { get; set; }
    public bool IsInstalled { get; set; }
    public string? DownloadUrl { get; set; }
    public string Version { get; set; } = "1.0";
    public string Author { get; set; } = "";
    public bool IsCustom { get; set; }
    public bool IsActive { get; set; }
    public DateTime? DateAdded { get; set; }
}

public class EngineProbeResult
{
    public bool Success { get; set; }
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "";
    public string? ErrorMessage { get; set; }
}

public class EnginesConfigFile
{
    public string ActiveEngineId { get; set; } = "stockfish-17";
    public string? SyzygyPath { get; set; }
    public List<EngineInfo> Engines { get; set; } = new();
}

public class EngineEvaluationLine
{
    public int MultiPvIndex { get; set; } = 1;
    public int Depth { get; set; }
    public int SelectiveDepth { get; set; }
    public double? Centipawns { get; set; }
    public int? MateInMoves { get; set; }
    public long Nodes { get; set; }
    public long Nps { get; set; }
    public List<string> PvMoves { get; set; } = new();

    public string BestMove => PvMoves.Count > 0 ? PvMoves[0] : "";

    public string FormattedScore
    {
        get
        {
            if (MateInMoves.HasValue)
            {
                return MateInMoves.Value > 0 ? $"M{MateInMoves.Value}" : $"-M{Math.Abs(MateInMoves.Value)}";
            }
            if (Centipawns.HasValue)
            {
                double cp = Centipawns.Value / 100.0;
                return cp >= 0 ? $"+{cp:F2}" : $"{cp:F2}";
            }
            return "0.00";
        }
    }

    public double WhiteWinPercentage
    {
        get
        {
            if (MateInMoves.HasValue)
            {
                return MateInMoves.Value > 0 ? 100.0 : 0.0;
            }
            if (Centipawns.HasValue)
            {
                // Standard Lichess winning chances formula
                double cp = Centipawns.Value;
                return 50.0 + 50.0 * (2.0 / (1.0 + Math.Exp(-0.00368208 * cp)) - 1.0);
            }
            return 50.0;
        }
    }
}
