namespace Caissalytics.Data;

public class ChessClubSettings
{
    public string ClubName { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";

    /// <summary>
    /// Shows the coach-oriented dashboard features (Printable Homework & Diagrams).
    /// </summary>
    public bool CoachFeaturesEnabled { get; set; }
}
