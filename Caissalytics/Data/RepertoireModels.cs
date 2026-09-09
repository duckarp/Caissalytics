using System.Text.Json.Serialization;

namespace Caissalytics.Data;

public class RepertoireMove
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Fen { get; set; } = string.Empty;
    public string MoveSan { get; set; } = string.Empty;
    public string MoveUci { get; set; } = string.Empty;
    public string Color { get; set; } = "white"; // "white" or "black"
    public string Status { get; set; } = "main"; // "main", "alt", "surprise"
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RepertoireTree
{
    public List<RepertoireMove> WhiteMoves { get; set; } = new();
    public List<RepertoireMove> BlackMoves { get; set; } = new();
}

public class RepertoireLine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "white"; // "white" or "black"
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<RepertoireLineMove> Moves { get; set; } = new();
}

public class RepertoireLineMove
{
    public string Fen { get; set; } = string.Empty;
    public string MoveSan { get; set; } = string.Empty;
    public string MoveUci { get; set; } = string.Empty;
    public int MoveNumber { get; set; }
}

public class RepertoireCollection
{
    public List<RepertoireMove> WhiteMoves { get; set; } = new();
    public List<RepertoireMove> BlackMoves { get; set; } = new();
    public List<RepertoireLine> Lines { get; set; } = new();
}

public enum TreeSourceType
{
    ActiveDatabase,
    MasterLibrary,
    UserGames,
    LichessMasters,
    LichessCommunity
}


// Lichess Opening Explorer API models
public class LichessExplorerResponse
{
    [JsonPropertyName("white")]
    public int White { get; set; }

    [JsonPropertyName("draws")]
    public int Draws { get; set; }

    [JsonPropertyName("black")]
    public int Black { get; set; }

    [JsonPropertyName("moves")]
    public List<LichessExplorerMove> Moves { get; set; } = new();

    [JsonPropertyName("topGames")]
    public List<LichessExplorerGame> TopGames { get; set; } = new();
}

public class LichessExplorerMove
{
    [JsonPropertyName("san")]
    public string San { get; set; } = string.Empty;

    [JsonPropertyName("uci")]
    public string Uci { get; set; } = string.Empty;

    [JsonPropertyName("white")]
    public int White { get; set; }

    [JsonPropertyName("draws")]
    public int Draws { get; set; }

    [JsonPropertyName("black")]
    public int Black { get; set; }

    [JsonPropertyName("averageRating")]
    public int? AverageRating { get; set; }
}

public class LichessExplorerGame
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("winner")]
    public string? Winner { get; set; }

    [JsonPropertyName("white")]
    public LichessPlayer White { get; set; } = new();

    [JsonPropertyName("black")]
    public LichessPlayer Black { get; set; } = new();

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("month")]
    public string? Month { get; set; }
}

public class LichessPlayer
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("rating")]
    public int? Rating { get; set; }
}
