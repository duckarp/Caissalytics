namespace Caissalytics.Core;

/// <summary>Outcome of merging one game into another.</summary>
/// <param name="StartPositionMismatch">The games start from different positions, so nothing was merged.</param>
/// <param name="MovesAdded">Moves the target did not have yet.</param>
public readonly record struct MergeResult(bool StartPositionMismatch, int MovesAdded);

/// <summary>
/// Merges games into one move tree the way ChessBase does: moves both games share are walked together,
/// and wherever the source goes its own way the rest of it is added as a variation. The target's lines
/// keep their order, so its main line stays the main line. Matching is by move order, not by position.
/// Each move counts the games whose main line played it (<see cref="MoveNode.GameCount"/>); a game merged
/// for the first time counts as one game along its main line.
/// </summary>
public static class GameMerger
{
    public static MergeResult Merge(GameTree target, GameTree source, string? label = null)
    {
        if (!SameStart(target.Root.Position, source.Root.Position))
        {
            return new MergeResult(true, 0);
        }

        if (!HasCounts(target.Root))
        {
            for (var node = target.Root; node.Children.Count > 0; node = node.Children[0])
            {
                node.Children[0].GameCount = 1;
            }
        }

        int added = MergeChildren(target.Root, source.Root, label, sourceMainline: true, HasCounts(source.Root));
        return new MergeResult(false, added);
    }

    /// <summary>
    /// Headers for a game merged from several: a tag the games agree on is kept, players and event that
    /// differ become <paramref name="various"/>, and other differing tags are dropped.
    /// </summary>
    public static void SetMergedHeaders(GameTree merged, IReadOnlyList<IReadOnlyDictionary<string, string>> games, string various)
    {
        foreach (string tag in merged.Headers.Keys.ToList())
        {
            if (tag is "SetUp" or "FEN") continue;
            string value = merged.Headers[tag];
            if (games.All(g => g.TryGetValue(tag, out var v) && v == value)) continue;

            switch (tag)
            {
                case "White" or "Black" or "Event":
                    merged.Headers[tag] = various;
                    break;
                case "Date":
                    merged.Headers[tag] = "????.??.??";
                    break;
                case "Result":
                    merged.Headers[tag] = "*";
                    break;
                default:
                    merged.Headers.Remove(tag);
                    break;
            }
        }
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

    private static int MergeChildren(MoveNode target, MoveNode source, string? label, bool sourceMainline, bool sourceCounted)
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
                match.GameCount += SourceCount(sourceChild, childMainline, sourceCounted);
                added += MergeChildren(match, sourceChild, label, childMainline, sourceCounted);
            }
            else
            {
                var copy = CopySubtree(sourceChild, target, childMainline, sourceCounted);
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
    private static MoveNode CopySubtree(MoveNode source, MoveNode parent, bool sourceMainline, bool sourceCounted)
    {
        var copy = new MoveNode(source.Move, source.San, source.Position, parent)
        {
            Comment = source.Comment,
            Eval = source.Eval,
            GameCount = SourceCount(source, sourceMainline, sourceCounted),
        };
        copy.Nags.AddRange(source.Nags);
        for (int i = 0; i < source.Children.Count; i++)
        {
            copy.Children.Add(CopySubtree(source.Children[i], copy, sourceMainline && i == 0, sourceCounted));
        }
        return copy;
    }

    // A game that was merged before brings its own counts; any other game is one game along its main line
    private static int SourceCount(MoveNode node, bool onMainline, bool sourceCounted) =>
        sourceCounted ? node.GameCount : onMainline ? 1 : 0;

    private static bool HasCounts(MoveNode node) => node.GameCount > 0 || node.Children.Any(HasCounts);

    private static bool SameMove(Move a, Move b) => a.From == b.From && a.To == b.To && a.Promotion == b.Promotion;

    private static bool SameStart(BoardPosition a, BoardPosition b) =>
        FenParser.ToFen(a, includeCounters: false) == FenParser.ToFen(b, includeCounters: false);
}
