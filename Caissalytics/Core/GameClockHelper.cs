using System.Globalization;

namespace Caissalytics.Core;

public record BoardClockState(
    string? WhiteClock,
    string? WhiteClockFormatted,
    string? BlackClock,
    string? BlackClockFormatted,
    bool IsWhiteTurn,
    bool HasClocks,
    bool WhiteLowTime,
    bool BlackLowTime,
    bool IsGameOver
);

public static class GameClockHelper
{
    public static bool HasClocks(GameTree? tree)
    {
        if (tree?.Root == null) return false;
        var cur = tree.Root;
        for (int i = 0; i < 15 && cur.Children.Count > 0; i++)
        {
            cur = cur.Children[0];
            if (!string.IsNullOrEmpty(cur.Clock)) return true;
        }
        return false;
    }

    public static BoardClockState GetClockState(GameTree? tree)
    {
        if (tree?.Root == null)
        {
            return new BoardClockState(null, null, null, null, true, false, false, false, false);
        }

        bool hasClocks = HasClocks(tree);
        var node = tree.CurrentNode;
        bool isWhiteTurn = node.Position.ActiveColor == PieceColor.White;

        // Check if game is over at current node
        bool isGameOver = MoveGenerator.IsCheckmate(node.Position) || MoveGenerator.IsStalemate(node.Position);
        if (!isGameOver && node.Children.Count == 0)
        {
            if (tree.Headers.TryGetValue("Result", out var res) &&
                (res == "1-0" || res == "0-1" || res == "1/2-1/2"))
            {
                isGameOver = true;
            }
        }

        if (!hasClocks)
        {
            return new BoardClockState(null, null, null, null, isWhiteTurn, false, false, false, isGameOver);
        }

        // 1. Find White's latest clock by walking up tree from current node
        string? whiteClock = null;
        string? whiteFormatted = null;
        var cur = node;
        while (cur != null && !cur.IsRoot)
        {
            if (cur.IsWhiteMove && !string.IsNullOrEmpty(cur.Clock))
            {
                whiteClock = cur.Clock;
                whiteFormatted = cur.FormattedClock;
                break;
            }
            cur = cur.Parent;
        }

        // If not found (e.g. at Root), look forward to first white move in mainline
        if (string.IsNullOrEmpty(whiteClock))
        {
            var firstWhite = tree.Root.Children.FirstOrDefault();
            if (firstWhite != null && !string.IsNullOrEmpty(firstWhite.Clock))
            {
                whiteClock = firstWhite.Clock;
                whiteFormatted = firstWhite.FormattedClock;
            }
        }

        // 2. Find Black's latest clock by walking up tree from current node
        string? blackClock = null;
        string? blackFormatted = null;
        cur = node;
        while (cur != null && !cur.IsRoot)
        {
            if (!cur.IsWhiteMove && !string.IsNullOrEmpty(cur.Clock))
            {
                blackClock = cur.Clock;
                blackFormatted = cur.FormattedClock;
                break;
            }
            cur = cur.Parent;
        }

        // If not found (e.g. at Root or after 1. e4), look forward to first black move in mainline
        if (string.IsNullOrEmpty(blackClock))
        {
            var firstWhite = tree.Root.Children.FirstOrDefault();
            var firstBlack = firstWhite?.Children.FirstOrDefault();
            if (firstBlack != null && !string.IsNullOrEmpty(firstBlack.Clock))
            {
                blackClock = firstBlack.Clock;
                blackFormatted = firstBlack.FormattedClock;
            }
        }

        bool whiteLowTime = IsLowTime(whiteClock);
        bool blackLowTime = IsLowTime(blackClock);

        return new BoardClockState(
            whiteClock,
            whiteFormatted,
            blackClock,
            blackFormatted,
            isWhiteTurn,
            true,
            whiteLowTime,
            blackLowTime,
            isGameOver
        );
    }

    public static bool IsLowTime(string? clock)
    {
        if (string.IsNullOrWhiteSpace(clock)) return false;
        var parts = clock.Trim().Split(':');
        if (parts.Length == 3)
        {
            if (int.TryParse(parts[0], out int h) && h > 0) return false;
            if (int.TryParse(parts[1], out int m) && m > 0) return false;
            if (double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double s))
            {
                return s <= 30.0;
            }
        }
        else if (parts.Length == 2)
        {
            if (int.TryParse(parts[0], out int m) && m > 0) return false;
            if (double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double s))
            {
                return s <= 30.0;
            }
        }
        return false;
    }
}
