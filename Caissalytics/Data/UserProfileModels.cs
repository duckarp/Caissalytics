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
}
