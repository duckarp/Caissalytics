using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Caissalytics.Core;

public static class HomeworkMarkdownHelper
{
    private static readonly Regex BoldRegex1 = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex BoldRegex2 = new(@"__(.+?)__", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex1 = new(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex2 = new(@"(?<!_)_(?!_)(.+?)(?<!_)_(?!_)", RegexOptions.Compiled);
    private static readonly Regex CodeRegex = new(@"`(.+?)`", RegexOptions.Compiled);
    private static readonly Regex NumberedListRegex = new(@"^\s*(\d+)\.\s+(.*)$", RegexOptions.Compiled);

    public static MarkupString ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new MarkupString(string.Empty);
        }

        // HTML encode to prevent any XSS
        string encoded = WebUtility.HtmlEncode(markdown.Replace("\r\n", "\n").Replace("\r", "\n"));
        var rawLines = encoded.Split('\n');

        var sb = new StringBuilder();
        bool inUl = false;
        bool inOl = false;
        bool inBlockquote = false;

        void CloseLists()
        {
            if (inUl)
            {
                sb.AppendLine("</ul>");
                inUl = false;
            }
            if (inOl)
            {
                sb.AppendLine("</ol>");
                inOl = false;
            }
        }

        void CloseBlockquote()
        {
            if (inBlockquote)
            {
                sb.AppendLine("</blockquote>");
                inBlockquote = false;
            }
        }

        for (int i = 0; i < rawLines.Length; i++)
        {
            string line = rawLines[i].TrimEnd();
            string trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                CloseLists();
                CloseBlockquote();
                continue;
            }

            // Horizontal rule
            if (trimmed == "---" || trimmed == "***" || trimmed == "___")
            {
                CloseLists();
                CloseBlockquote();
                sb.AppendLine("<hr class=\"notes-divider\" />");
                continue;
            }

            // Headings
            if (trimmed.StartsWith("### "))
            {
                CloseLists();
                CloseBlockquote();
                string text = FormatInline(trimmed[4..]);
                sb.AppendLine($"<h5 class=\"notes-h3\">{text}</h5>");
                continue;
            }
            if (trimmed.StartsWith("## "))
            {
                CloseLists();
                CloseBlockquote();
                string text = FormatInline(trimmed[3..]);
                sb.AppendLine($"<h4 class=\"notes-h2\">{text}</h4>");
                continue;
            }
            if (trimmed.StartsWith("# "))
            {
                CloseLists();
                CloseBlockquote();
                string text = FormatInline(trimmed[2..]);
                sb.AppendLine($"<h3 class=\"notes-h1\">{text}</h3>");
                continue;
            }

            // Blockquote
            if (trimmed.StartsWith("&gt; ") || trimmed.StartsWith("> "))
            {
                CloseLists();
                int skip = trimmed.StartsWith("&gt; ") ? 5 : 2;
                string quoteText = FormatInline(trimmed[skip..]);
                if (!inBlockquote)
                {
                    sb.AppendLine("<blockquote class=\"notes-callout\">");
                    inBlockquote = true;
                }
                sb.AppendLine($"<p>{quoteText}</p>");
                continue;
            }
            else
            {
                CloseBlockquote();
            }

            // Bullet list (- , * , • )
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("• "))
            {
                if (inOl) CloseLists();
                if (!inUl)
                {
                    sb.AppendLine("<ul class=\"notes-list\">");
                    inUl = true;
                }
                string liText = FormatInline(trimmed[2..]);
                sb.AppendLine($"<li>{liText}</li>");
                continue;
            }

            // Numbered list
            var numMatch = NumberedListRegex.Match(trimmed);
            if (numMatch.Success)
            {
                if (inUl) CloseLists();
                if (!inOl)
                {
                    sb.AppendLine("<ol class=\"notes-ordered-list\">");
                    inOl = true;
                }
                string liText = FormatInline(numMatch.Groups[2].Value);
                sb.AppendLine($"<li>{liText}</li>");
                continue;
            }

            // Standard line / paragraph
            CloseLists();
            string pText = FormatInline(trimmed);
            sb.AppendLine($"<p class=\"notes-p\">{pText}</p>");
        }

        CloseLists();
        CloseBlockquote();

        return new MarkupString(sb.ToString());
    }

    private static string FormatInline(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Bold
        input = BoldRegex1.Replace(input, "<strong>$1</strong>");
        input = BoldRegex2.Replace(input, "<strong>$1</strong>");

        // Italic
        input = ItalicRegex1.Replace(input, "<em>$1</em>");
        input = ItalicRegex2.Replace(input, "<em>$1</em>");

        // Code / moves
        input = CodeRegex.Replace(input, "<code class=\"notes-code\">$1</code>");

        return input;
    }
}
