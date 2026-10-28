using System.Text;

namespace DcLedger.Application;

/// <summary>
/// Makes untrusted text safe to put in a GitHub issue (ADR-0003): one line, Markdown and HTML inert, and @-mentions
/// that can't notify anyone. Case numbers are reduced to <c>[A-Za-z0-9-]</c>.
/// </summary>
internal static class IssueText
{
    /// <summary>Longest case number kept; anything longer is cut.</summary>
    internal const int MaxCaseNumberLength = 40;

    /// <summary>Text for a Markdown paragraph or table cell. Empty input renders as an em dash.</summary>
    public static string Inline(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "—";
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (IsInvisible(c))
            {
                continue;
            }

            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            switch (c)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '@':
                    builder.Append("@&#8203;"); // a zero-width space after @ stops the mention, as Dependabot does
                    break;
                case '\\' or '`' or '*' or '_' or '[' or ']' or '(' or ')' or '{' or '}' or '#' or '|' or '~' or '!' or ':':
                    builder.Append('\\').Append(c);
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.Length == 0 ? "—" : builder.ToString();
    }

    /// <summary>Text for an issue title, which GitHub shows as plain text: one line, mentions broken with U+200B.</summary>
    public static string Title(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (IsInvisible(c))
            {
                continue;
            }

            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
            if (c == '@')
            {
                builder.Append('\u200B');
            }
        }

        return builder.ToString();
    }

    /// <summary>The case number with everything outside <c>[A-Za-z0-9-]</c> removed, or null when nothing is left.</summary>
    public static string? CaseNumber(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var kept = string.Concat(text.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').Take(MaxCaseNumberLength));
        return kept.Trim('-').Length == 0 ? null : kept;
    }

    /// <summary>A YAML double-quoted scalar: control characters become spaces, invisible ones are dropped, backslash and quote escaped.</summary>
    public static string YamlQuoted(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder("\"", text.Length + 2);
        foreach (var c in text)
        {
            if (IsInvisible(c))
            {
                continue;
            }

            if (char.IsControl(c))
            {
                builder.Append(' ');
                continue;
            }

            if (c is '\\' or '"')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.Append('"').ToString();
    }

    /// <summary>Zero-width and bidirectional-control characters, which can hide or reorder text.</summary>
    private static bool IsInvisible(char c) =>
        c is '\u200B' or '\u200C' or '\u200D' or '\u2060' or '\uFEFF' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069');
}
