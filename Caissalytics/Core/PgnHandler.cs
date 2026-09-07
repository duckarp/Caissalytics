using System.Text;
using System.Text.RegularExpressions;

namespace Caissalytics.Core;

public static class PgnHandler
{
    public static string ExportPgn(GameTree tree)
    {
        var sb = new StringBuilder();

        // 1. Headers
        foreach (var (k, v) in tree.Headers)
        {
            sb.AppendLine($"[{k} \"{v}\"]");
        }
        sb.AppendLine();

        // 2. Movetext
        if (tree.Root.Children.Count > 0)
        {
            FormatNodeChildren(tree.Root, sb, true);
        }

        sb.Append(' ');
        sb.Append(tree.Headers.TryGetValue("Result", out var res) ? res : "*");
        sb.AppendLine();

        return sb.ToString();
    }

    private static void FormatNodeChildren(MoveNode parent, StringBuilder sb, bool isMainline)
    {
        if (parent.Children.Count == 0) return;

        // First child is the mainline continuation
        var mainChild = parent.Children[0];
        FormatSingleNode(mainChild, sb);

        // Subsequent children are variations
        for (int i = 1; i < parent.Children.Count; i++)
        {
            sb.Append(" (");
            var varChild = parent.Children[i];
            FormatSingleNode(varChild, sb, forceMoveNumber: true);
            FormatNodeChildren(varChild, sb, false);
            sb.Append(')');
        }

        // Continue mainline
        FormatNodeChildren(mainChild, sb, isMainline);
    }

    private static void FormatSingleNode(MoveNode node, StringBuilder sb, bool forceMoveNumber = false)
    {
        sb.Append(' ');
        if (node.IsWhiteMove || forceMoveNumber)
        {
            sb.Append(node.MoveNumber);
            sb.Append(node.IsWhiteMove ? ". " : "... ");
        }

        sb.Append(node.San);

        foreach (var nag in node.Nags)
        {
            sb.Append($" ${nag}");
        }

        if (!string.IsNullOrWhiteSpace(node.Comment))
        {
            sb.Append($" {{{node.Comment.Trim()}}}");
        }
    }

    public static GameTree ImportPgn(string pgnText)
    {
        if (string.IsNullOrWhiteSpace(pgnText))
            return new GameTree();

        var headerRegex = new Regex(@"\[(\w+)\s+""([^""]*)""\]");
        var matches = headerRegex.Matches(pgnText);

        string? startFen = null;
        foreach (Match m in matches)
        {
            if (string.Equals(m.Groups[1].Value, "FEN", StringComparison.OrdinalIgnoreCase))
            {
                startFen = m.Groups[2].Value;
                break;
            }
        }

        var tree = new GameTree(startFen);
        foreach (Match m in matches)
        {
            tree.Headers[m.Groups[1].Value] = m.Groups[2].Value;
        }

        // Remove headers
        string movetext = headerRegex.Replace(pgnText, "").Trim();

        // Tokenize movetext
        var tokens = TokenizeMovetext(movetext);
        ParseTokensIntoTree(tree, tokens);

        tree.GoToStart();
        return tree;
    }

    private static List<string> TokenizeMovetext(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        int len = text.Length;

        while (i < len)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '{')
            {
                int end = text.IndexOf('}', i);
                if (end == -1) end = len - 1;
                tokens.Add(text.Substring(i, end - i + 1));
                i = end + 1;
            }
            else if (c == '(' || c == ')')
            {
                tokens.Add(c.ToString());
                i++;
            }
            else
            {
                int start = i;
                while (i < len && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ')' && text[i] != '{')
                {
                    i++;
                }
                tokens.Add(text.Substring(start, i - start));
            }
        }

        return tokens;
    }

    private static void ParseTokensIntoTree(GameTree tree, List<string> tokens)
    {
        var branchStack = new Stack<MoveNode>();

        foreach (var tok in tokens)
        {
            if (tok == "(")
            {
                // Push current parent onto stack to branch
                if (tree.CurrentNode.Parent != null)
                {
                    branchStack.Push(tree.CurrentNode);
                    tree.NavigateTo(tree.CurrentNode.Parent);
                }
            }
            else if (tok == ")")
            {
                // Pop back to continuation of previous branch
                if (branchStack.Count > 0)
                {
                    var returnNode = branchStack.Pop();
                    tree.NavigateTo(returnNode);
                }
            }
            else if (tok.StartsWith("{") && tok.EndsWith("}"))
            {
                string comment = tok.Substring(1, tok.Length - 2).Trim();
                if (!tree.CurrentNode.IsRoot)
                {
                    tree.CurrentNode.Comment = string.IsNullOrEmpty(tree.CurrentNode.Comment)
                        ? comment
                        : $"{tree.CurrentNode.Comment} {comment}";
                }
            }
            else if (tok.StartsWith("$") && int.TryParse(tok.Substring(1), out int nag))
            {
                if (!tree.CurrentNode.IsRoot && !tree.CurrentNode.Nags.Contains(nag))
                {
                    tree.CurrentNode.Nags.Add(nag);
                }
            }
            else if (tok.EndsWith(".") || Regex.IsMatch(tok, @"^\d+\.*$"))
            {
                // Move numbers like 1. or 1... - skip
                continue;
            }
            else if (tok == "1-0" || tok == "0-1" || tok == "1/2-1/2" || tok == "*")
            {
                tree.Headers["Result"] = tok;
            }
            else
            {
                // Must be a move SAN
                var move = SanParser.ParseSan(tree.CurrentNode.Position, tok);
                if (!move.IsEmpty)
                {
                    tree.AddMove(move);
                }
            }
        }
    }
}
