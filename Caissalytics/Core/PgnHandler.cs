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
            sb.AppendLine($"[{k} \"{EscapeHeaderValue(v)}\"]");
        }
        sb.AppendLine();

        // 2. Movetext
        if (tree.Root.Children.Count > 0)
        {
            FormatNodeChildren(tree.Root, sb, true, forceMoveNumber: false);
        }

        sb.Append(' ');
        sb.Append(tree.Headers.TryGetValue("Result", out var res) ? res : "*");
        sb.AppendLine();

        return sb.ToString();
    }

    private static void FormatNodeChildren(MoveNode parent, StringBuilder sb, bool isMainline, bool forceMoveNumber = false)
    {
        if (parent.IsRoot && !string.IsNullOrWhiteSpace(parent.Comment))
        {
            sb.Append($"{{{SanitizeComment(parent.Comment)}}}");
        }

        if (parent.Children.Count == 0) return;

        // First child is the mainline continuation
        var mainChild = parent.Children[0];
        FormatSingleNode(mainChild, sb, forceMoveNumber: forceMoveNumber);

        bool hadInterruption = false;
        // Subsequent children are variations
        for (int i = 1; i < parent.Children.Count; i++)
        {
            sb.Append(" (");
            var varChild = parent.Children[i];
            FormatSingleNode(varChild, sb, forceMoveNumber: true);
            FormatNodeChildren(varChild, sb, false);
            sb.Append(')');
            hadInterruption = true;
        }

        if (!string.IsNullOrWhiteSpace(mainChild.Comment))
        {
            hadInterruption = true;
        }

        // Continue mainline
        FormatNodeChildren(mainChild, sb, isMainline, forceMoveNumber: hadInterruption);
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

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(node.Clock)) parts.Add($"[%clk {node.Clock}]");
        if (node.GameCount > 1) parts.Add($"[%games {node.GameCount}]");
        if (!string.IsNullOrWhiteSpace(node.Comment)) parts.Add(SanitizeComment(node.Comment));
        if (parts.Count > 0)
        {
            sb.Append($" {{{string.Join(' ', parts)}}}");
        }
    }

    // PGN header values escape '"' and '\' with a backslash
    private static string EscapeHeaderValue(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string UnescapeHeaderValue(string value) => Regex.Replace(value, @"\\(.)", "$1");

    // A brace comment ends at the first '}' and PGN has no escape for it, so it can't appear inside
    private static string SanitizeComment(string comment) => comment.Trim().Replace('}', ')');

    public static GameTree ImportPgn(string pgnText)
    {
        if (string.IsNullOrWhiteSpace(pgnText))
            return new GameTree();

        // Read the tag pairs at the start of the game; tags inside comments or later games are not headers
        var headers = new List<(string Key, string Value)>();
        int pos = 0;
        while (true)
        {
            var m = LeadingHeaderRegex.Match(pgnText, pos);
            if (!m.Success) break;
            headers.Add((m.Groups[1].Value, UnescapeHeaderValue(m.Groups[2].Value)));
            pos = m.Index + m.Length;
        }

        string? startFen = headers
            .Where(h => string.Equals(h.Key, "FEN", StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Value)
            .FirstOrDefault();

        var tree = new GameTree(startFen);
        foreach (var (key, value) in headers)
        {
            tree.Headers[key] = value;
        }

        // Any other whole-line tags (e.g. a following game's headers) are dropped as before
        string movetext = StandaloneHeaderLineRegex.Replace(pgnText[pos..], "").Trim();

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

            if (c == '%' && (i == 0 || text[i - 1] == '\n'))
            {
                // Escape line: ignored entirely
                int eol = text.IndexOf('\n', i);
                i = eol == -1 ? len : eol + 1;
                continue;
            }

            if (c == ';')
            {
                // Rest-of-line comment, stored like a brace comment
                int eol = text.IndexOf('\n', i);
                if (eol == -1) eol = len;
                tokens.Add("{" + text.Substring(i + 1, eol - i - 1).Trim().Replace('}', ')') + "}");
                i = eol;
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
                while (i < len && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ')' && text[i] != '{' && text[i] != ';')
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
        // > 0 while inside a line that hit an illegal move: its remaining moves would be replayed
        // from the wrong position, so they are skipped until that variation closes
        int skipDepth = 0;

        foreach (var tok in tokens)
        {
            if (skipDepth > 0)
            {
                if (tok == "(")
                {
                    skipDepth++;
                    continue;
                }
                if (tok != ")")
                {
                    if (tok == "1-0" || tok == "0-1" || tok == "1/2-1/2" || tok == "*")
                        tree.Headers["Result"] = tok;
                    continue;
                }
                skipDepth--;
                if (skipDepth > 0) continue;
                // This ')' closes the broken variation: handle it normally below
            }

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
                ParseAndAttachComment(tree.CurrentNode, comment);
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
                // Must be a move SAN. Strip a glued leading move number first — tournament PGNs
                // often have no space after it ("1.e4" -> "e4", "12...Nf6" -> "Nf6"); without this
                // SanParser fails to parse the token and the move is silently dropped.
                string san = MoveNumberPrefixRegex.Replace(tok, "");
                if (string.IsNullOrWhiteSpace(san))
                    continue;

                var move = SanParser.ParseSan(tree.CurrentNode.Position, san);
                if (!move.IsEmpty)
                {
                    tree.AddMove(move);
                }
                else if (SanLikeRegex.IsMatch(san))
                {
                    // A real move that is illegal here (corrupt or mis-transcribed game)
                    skipDepth = 1;
                }
            }
        }
    }

    private static readonly Regex ClkRegex = new(@"\[%clk\s+([0-9:.]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EvalRegex = new(@"\[%eval\s+([#+-]?[0-9.]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GamesRegex = new(@"\[%games\s+([0-9]{1,6})\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MachineTagRegex = new(@"\[%[a-zA-Z0-9_]+(?:\s+[^\]]*)?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MoveNumberPrefixRegex = new(@"^\d+\.+", RegexOptions.Compiled);
    private static readonly Regex LeadingHeaderRegex = new(@"\G\s*\[(\w+)\s+""((?:[^""\\]|\\.)*)""\]", RegexOptions.Compiled);
    private static readonly Regex StandaloneHeaderLineRegex = new(@"^[ \t]*\[\w+\s+""(?:[^""\\]|\\.)*""\][ \t]*\r?$", RegexOptions.Compiled | RegexOptions.Multiline);
    // Tokens shaped like a SAN move; other unparseable tokens (stray annotations etc.) are just ignored
    private static readonly Regex SanLikeRegex = new(@"^(?:[KQRBN][a-h]?[1-8]?x?[a-h][1-8]|[a-h](?:x[a-h])?[1-8](?:=?[QRBNqrbn])?|O-O(?:-O)?|0-0(?:-0)?)[+#]?[!?]*$", RegexOptions.Compiled);

    /// <summary>
    /// Collapses spaces and tabs within each line but keeps line breaks (at most one empty line in a row),
    /// so comments written with Enter keep their layout.
    /// </summary>
    private static string NormalizeCommentWhitespace(string comment)
    {
        var lines = comment.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(line => HorizontalWhitespaceRegex.Replace(line, " ").Trim());
        string joined = string.Join("\n", lines);
        return ExtraBlankLinesRegex.Replace(joined, "\n\n").Trim();
    }

    private static readonly Regex HorizontalWhitespaceRegex = new(@"[^\S\n]+", RegexOptions.Compiled);
    private static readonly Regex ExtraBlankLinesRegex = new(@"\n{3,}", RegexOptions.Compiled);

    private static void ParseAndAttachComment(MoveNode node, string rawComment)
    {
        if (string.IsNullOrWhiteSpace(rawComment)) return;

        // Extract clock time if present
        var clkMatch = ClkRegex.Match(rawComment);
        if (clkMatch.Success)
        {
            node.Clock = clkMatch.Groups[1].Value;
            rawComment = ClkRegex.Replace(rawComment, "");
        }

        // Extract engine eval if present
        var evalMatch = EvalRegex.Match(rawComment);
        if (evalMatch.Success)
        {
            string evalVal = evalMatch.Groups[1].Value;
            if (!evalVal.StartsWith("+") && !evalVal.StartsWith("-") && !evalVal.StartsWith("#"))
            {
                evalVal = "+" + evalVal;
            }
            node.Eval = evalVal;
            rawComment = EvalRegex.Replace(rawComment, "");
        }

        // How many merged games played this move (see GameMerger)
        var gamesMatch = GamesRegex.Match(rawComment);
        if (gamesMatch.Success)
        {
            node.GameCount = int.Parse(gamesMatch.Groups[1].Value);
        }

        // Strip any remaining machine annotations (e.g. [%emt ...], [%csl ...], [%cal ...])
        rawComment = MachineTagRegex.Replace(rawComment, "");

        // Keep real human commentary, including the line breaks the author typed
        string cleaned = NormalizeCommentWhitespace(rawComment);
        if (!string.IsNullOrEmpty(cleaned))
        {
            node.Comment = string.IsNullOrEmpty(node.Comment)
                ? cleaned
                : $"{node.Comment} {cleaned}";
        }
    }
}
