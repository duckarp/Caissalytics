namespace Caissalytics.Core;

public class GameTree
{
    public MoveNode Root { get; private set; }
    public MoveNode CurrentNode { get; private set; }
    public Dictionary<string, string> Headers { get; } = new();

    public event Action? PositionChanged;

    public bool CanGoBack => CurrentNode.Parent != null;
    public bool CanGoForward => CurrentNode.Children.Count > 0;

    public GameTree(string? startFen = null)
    {
        var initialPos = FenParser.Parse(startFen ?? BoardPosition.StartFen);
        Root = new MoveNode(Move.Empty, "", initialPos);
        CurrentNode = Root;

        Headers["Event"] = "Casual Analysis";
        Headers["Site"] = "Caissalytics";
        Headers["Date"] = DateTime.UtcNow.ToString("yyyy.MM.dd");
        Headers["Round"] = "1";
        Headers["White"] = "White";
        Headers["Black"] = "Black";
        Headers["Result"] = "*";
    }

    public MoveNode AddMove(Move move)
    {
        var pos = CurrentNode.Position;
        string san = SanParser.ToSan(pos, move);
        var nextPos = MoveGenerator.ApplyMove(pos, move);

        // Check if move already exists as a child
        var existing = CurrentNode.Children.FirstOrDefault(c => c.Move.From == move.From && c.Move.To == move.To && c.Move.Promotion == move.Promotion);
        if (existing != null)
        {
            CurrentNode = existing;
            PositionChanged?.Invoke();
            return existing;
        }

        var newNode = new MoveNode(move, san, nextPos, CurrentNode);
        CurrentNode.Children.Add(newNode);
        CurrentNode = newNode;

        PositionChanged?.Invoke();
        return newNode;
    }

    public MoveNode? AddMoveSan(string san)
    {
        var move = SanParser.ParseSan(CurrentNode.Position, san);
        if (move.IsEmpty) return null;
        return AddMove(move);
    }

    public bool GoBack()
    {
        if (CurrentNode.Parent != null)
        {
            CurrentNode = CurrentNode.Parent;
            PositionChanged?.Invoke();
            return true;
        }
        return false;
    }

    public bool GoForward(int childIndex = 0)
    {
        if (childIndex >= 0 && childIndex < CurrentNode.Children.Count)
        {
            CurrentNode = CurrentNode.Children[childIndex];
            PositionChanged?.Invoke();
            return true;
        }
        return false;
    }

    public void GoToStart()
    {
        CurrentNode = Root;
        PositionChanged?.Invoke();
    }

    public void GoToEnd()
    {
        while (CurrentNode.Children.Count > 0)
        {
            CurrentNode = CurrentNode.Children[0];
        }
        PositionChanged?.Invoke();
    }

    public void NavigateTo(MoveNode target)
    {
        CurrentNode = target;
        PositionChanged?.Invoke();
    }

    public void PromoteVariation(MoveNode node)
    {
        if (node.Parent == null) return;
        int idx = node.Parent.Children.IndexOf(node);
        if (idx > 0)
        {
            node.Parent.Children.RemoveAt(idx);
            node.Parent.Children.Insert(0, node);
            PositionChanged?.Invoke();
        }
    }

    public void DeleteSubtree(MoveNode node)
    {
        if (node.Parent == null) return;
        var parent = node.Parent;
        parent.Children.Remove(node);
        if (CurrentNode == node || IsDescendant(node, CurrentNode))
        {
            CurrentNode = parent;
        }
        PositionChanged?.Invoke();
    }

    private static bool IsDescendant(MoveNode ancestor, MoveNode node)
    {
        var curr = node;
        while (curr.Parent != null)
        {
            if (curr.Parent == ancestor) return true;
            curr = curr.Parent;
        }
        return false;
    }
}
