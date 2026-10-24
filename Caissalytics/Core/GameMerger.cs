namespace Caissalytics.Core;

/// <summary>Outcome of merging one game into another.</summary>
/// <param name="StartPositionMismatch">The games start from different positions, so nothing was merged.</param>
/// <param name="MovesAdded">Moves the target did not have yet.</param>
public readonly record struct MergeResult(bool StartPositionMismatch, int MovesAdded);

/// <summary>
/// Merges games into one move tree the way ChessBase does: moves both games share are walked together,
/// and wherever the source goes its own way the rest of it is added as a variation. The target's lines
/// keep their order, so its main line stays the main line. Matching is by move order, not by position.
/// </summary>
public static class GameMerger
{
    public static MergeResult Merge(GameTree target, GameTree source, string? label = null)
    {
        if (!SameStart(target.Root.Position, source.Root.Position))
        {
            return new MergeResult(true, 0);
        }

        int added = MergeChildren(target.Root, source.Root, label, sourceMainline: true);
        return new MergeResult(false, added);
    }

    /// <summary>"White – Black, Event Year (Result)", leaving out what the headers don't say.</summary>
    public static string BuildLabel(IReadOnlyDictionary<string, string> headers)
    {
        string? Tag(string name) => headers.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) && !v.Trim().All(c => c == '?')
            ? v.Trim()
            : null;

        var parts = new List<string>();
        string? white = Tag("White");
        string? black = Tag("Black");
        if (white != null || black != null)
        {
            parts.Add($"{white ?? "?"} – {black ?? "?"}");
        }

        string? year = Tag("Date") is { Length: >= 4 } date && date[..4].All(char.IsDigit) ? date[..4] : null;
        string eventAndYear = string.Join(" ", new[] { Tag("Event"), year }.Where(s => s != null));
        if (eventAndYear.Length > 0)
        {
            parts.Add(eventAndYear);
        }

        string label = string.Join(", ", parts);
        string? result = Tag("Result");
        if (result != null && result != "*")
        {
            label = label.Length > 0 ? $"{label} ({result})" : result;
        }
        return label;
    }

    private static int MergeChildren(MoveNode target, MoveNode source, string? label, bool sourceMainline)
    {
        int added = 0;
        for (int i = 0; i < source.Children.Count; i++)
        {
            var sourceChild = source.Children[i];
            bool childMainline = sourceMainline && i == 0;
            var match = target.Children.FirstOrDefault(c => SameMove(c.Move, sourceChild.Move));
            if (match != null)
            {
                CopyMissingAnnotations(match, sourceChild);
                added += MergeChildren(match, sourceChild, label, childMainline);
            }
            else
            {
                var copy = CopySubtree(sourceChild, target);
                // The game's own departure from the target is where it gets named
                if (childMainline && !string.IsNullOrWhiteSpace(label))
                {
                    copy.Comment = string.IsNullOrWhiteSpace(copy.Comment) ? label : $"{label} {copy.Comment}";
                }
                target.Children.Add(copy);
                added += GameTree.CountMoves(copy);
            }
        }
        return added;
    }

    // An annotated copy of a game merged into a bare one brings its comments along; the target's own win
    private static void CopyMissingAnnotations(MoveNode target, MoveNode source)
    {
        if (string.IsNullOrWhiteSpace(target.Comment)) target.Comment = source.Comment;
        if (target.Nags.Count == 0) target.Nags.AddRange(source.Nags);
        target.Eval ??= source.Eval;
    }

    // Clock times belong to the source game's timing, not to the analysis they join
    private static MoveNode CopySubtree(MoveNode source, MoveNode parent)
    {
        var copy = new MoveNode(source.Move, source.San, source.Position, parent)
        {
            Comment = source.Comment,
            Eval = source.Eval,
        };
        copy.Nags.AddRange(source.Nags);
        foreach (var child in source.Children)
        {
            copy.Children.Add(CopySubtree(child, copy));
        }
        return copy;
    }

    private static bool SameMove(Move a, Move b) => a.From == b.From && a.To == b.To && a.Promotion == b.Promotion;

    private static bool SameStart(BoardPosition a, BoardPosition b) =>
        FenParser.ToFen(a, includeCounters: false) == FenParser.ToFen(b, includeCounters: false);
}
