namespace Caissalytics.Core;

public class MoveNode
{
    public Guid Id { get; } = Guid.NewGuid();
    public Move Move { get; set; }
    public string San { get; set; } = "";
    public BoardPosition Position { get; set; }
    public MoveNode? Parent { get; set; }
    public List<MoveNode> Children { get; } = new();

    public string? Comment { get; set; }
    public string? Clock { get; set; }
    public string? Eval { get; set; }
    public List<int> Nags { get; } = new();

    public string? FormattedClock
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Clock)) return null;
            string c = Clock.Trim();
            if (c.StartsWith("0:") && c.Length > 2)
            {
                c = c[2..];
                if (c.StartsWith("0") && c.Length > 1 && c[1] != ':')
                {
                    c = c[1..];
                }
            }
            return c;
        }
    }

    public bool IsRoot => Parent == null;
    public bool IsMainline => Parent == null || (Parent.Children.Count > 0 && Parent.Children[0] == this);

    // Active color in Position is who is to move NEXT. If active is Black, White just moved!
    public bool IsWhiteMove => Position.ActiveColor == PieceColor.Black;
    public int MoveNumber => IsWhiteMove ? Position.FullmoveNumber : Position.FullmoveNumber - 1;

    public MoveNode(Move move, string san, BoardPosition position, MoveNode? parent = null)
    {
        Move = move;
        San = san;
        Position = position;
        Parent = parent;
    }

    public string NagSymbol()
    {
        if (Nags.Count == 0) return "";
        return Nags[0] switch
        {
            1 => "!",
            2 => "?",
            3 => "!!",
            4 => "??",
            5 => "!?",
            6 => "?!",
            10 => "=",
            14 => "+=",
            15 => "=+",
            16 => "±",
            17 => "∓",
            18 => "+-",
            19 => "-+",
            _ => $"${Nags[0]}"
        };
    }
}
