namespace Caissalytics.Core;

public enum DrawReason
{
    None,
    ThreefoldRepetition,
    FiftyMoveRule,
    InsufficientMaterial
}

/// <summary>Draws other than stalemate. Callers check checkmate/stalemate first.</summary>
public static class DrawRules
{
    public static DrawReason Detect(MoveNode node)
    {
        var pos = node.Position;
        if (IsInsufficientMaterial(pos)) return DrawReason.InsufficientMaterial;
        if (pos.HalfmoveClock >= 100) return DrawReason.FiftyMoveRule;
        if (IsThreefoldRepetition(node)) return DrawReason.ThreefoldRepetition;
        return DrawReason.None;
    }

    /// <summary>
    /// Same position (side to move, castling and en passant rights included via the Zobrist key)
    /// reached three times along the path to this node. Only positions since the last capture or
    /// pawn move can repeat, so the walk stops there.
    /// </summary>
    public static bool IsThreefoldRepetition(MoveNode node)
    {
        ulong key = node.Position.ZobristKey;
        int occurrences = 1;
        int pliesBack = node.Position.HalfmoveClock;

        var current = node.Parent;
        while (current != null && pliesBack-- > 0)
        {
            if (current.Position.ZobristKey == key && ++occurrences >= 3)
                return true;
            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// Neither side can ever mate: K v K, K+minor v K, or only same-coloured bishops besides the kings.
    /// </summary>
    public static bool IsInsufficientMaterial(BoardPosition pos)
    {
        int knights = 0;
        int lightBishops = 0;
        int darkBishops = 0;

        for (int sq = 0; sq < 64; sq++)
        {
            var piece = pos[(Square)sq];
            switch (piece.Type)
            {
                case PieceType.None:
                case PieceType.King:
                    break;
                case PieceType.Knight:
                    knights++;
                    break;
                case PieceType.Bishop:
                    // a1 (index 0) is dark: dark squares have even file + rank
                    if (((sq & 7) + (sq >> 3)) % 2 == 0) darkBishops++;
                    else lightBishops++;
                    break;
                default:
                    return false; // pawn, rook or queen
            }
        }

        int bishops = lightBishops + darkBishops;
        if (knights + bishops <= 1) return true;
        return knights == 0 && (lightBishops == 0 || darkBishops == 0);
    }

    public static string? Termination(DrawReason reason) => reason switch
    {
        DrawReason.ThreefoldRepetition => "Threefold repetition",
        DrawReason.FiftyMoveRule => "Fifty-move rule",
        DrawReason.InsufficientMaterial => "Insufficient material",
        _ => null
    };
}
