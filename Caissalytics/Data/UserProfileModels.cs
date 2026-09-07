namespace Caissalytics.Data;

public class UserProfile
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FideId { get; set; } = "";
    public string LichessUsername { get; set; } = "";
    public string ChessComUsername { get; set; } = "";

    public string FullName
    {
        get
        {
            string name = $"{FirstName} {LastName}".Trim();
            return string.IsNullOrEmpty(name) ? "Anonymous Player" : name;
        }
    }

    public bool HasFideId => !string.IsNullOrWhiteSpace(FideId);
    public string? FideProfileUrl => HasFideId ? $"https://ratings.fide.com/profile/{FideId.Trim()}" : null;

    public bool HasLichess => !string.IsNullOrWhiteSpace(LichessUsername);
    public string? LichessProfileUrl => HasLichess ? $"https://lichess.org/@/{LichessUsername.Trim()}" : null;

    public bool HasChessCom => !string.IsNullOrWhiteSpace(ChessComUsername);
    public string? ChessComProfileUrl => HasChessCom ? $"https://www.chess.com/member/{ChessComUsername.Trim()}" : null;

    public bool MatchesPlayer(string? playerName, string? fideId = null)
    {
        if (string.IsNullOrWhiteSpace(playerName) && string.IsNullOrWhiteSpace(fideId))
        {
            return false;
        }

        // 1. FIDE ID matching
        if (!string.IsNullOrWhiteSpace(fideId) && HasFideId)
        {
            if (string.Equals(fideId.Trim(), FideId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(playerName))
        {
            return false;
        }

        string cleanName = playerName.Trim();

        // 2. Online Handles matching (Lichess & Chess.com)
        if (HasLichess && string.Equals(cleanName, LichessUsername.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HasChessCom && string.Equals(cleanName, ChessComUsername.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3. Full Name matching
        bool hasFirst = !string.IsNullOrWhiteSpace(FirstName);
        bool hasLast = !string.IsNullOrWhiteSpace(LastName);

        if (hasFirst && hasLast)
        {
            string first = FirstName.Trim();
            string last = LastName.Trim();

            // "FirstName LastName" e.g. "Tomas Kovac"
            if (string.Equals(cleanName, $"{first} {last}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // "LastName, FirstName" e.g. "Kovac, Tomas"
            if (string.Equals(cleanName, $"{last}, {first}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // "LastName FirstName" e.g. "Kovac Tomas"
            if (string.Equals(cleanName, $"{last} {first}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Contains both name parts (e.g. "GM Tomas Kovac" or "Kovac, Tomas (SVK)")
            if (first.Length >= 2 && last.Length >= 2)
            {
                if (cleanName.Contains(last, StringComparison.OrdinalIgnoreCase) &&
                    cleanName.Contains(first, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Also support "LastName, F." initial format e.g. "Kovac, T."
                if (cleanName.StartsWith(last, StringComparison.OrdinalIgnoreCase) &&
                    cleanName.Contains($"{first[0]}."))
                {
                    return true;
                }
            }
        }
        else if (hasLast && LastName.Trim().Length >= 3)
        {
            if (string.Equals(cleanName, LastName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        else if (hasFirst && FirstName.Trim().Length >= 3)
        {
            if (string.Equals(cleanName, FirstName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
