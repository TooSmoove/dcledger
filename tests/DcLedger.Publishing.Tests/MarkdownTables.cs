using System.Text.RegularExpressions;

namespace DcLedger.Publishing.Tests;

/// <summary>Reads the pipe tables of a Markdown document, such as <c>docs/data-dictionary.md</c>, for contract tests.</summary>
internal static partial class MarkdownTables
{
    /// <summary>
    /// The body rows of the first table after the heading line <paramref name="heading"/> (matched exactly), each as its
    /// trimmed cells. The header row and the <c>|---|</c> separator are skipped.
    /// </summary>
    public static IReadOnlyList<string[]> After(string markdown, string heading)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var start = Array.IndexOf(lines, heading);
        if (start < 0)
        {
            throw new InvalidOperationException($"No heading line \"{heading}\" in the document.");
        }

        var table = lines
            .Skip(start + 1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .SkipWhile(line => !line.StartsWith('|'))
            .TakeWhile(line => line.StartsWith('|'))
            .ToList();
        if (table.Count < 2)
        {
            throw new InvalidOperationException($"No table under \"{heading}\".");
        }

        return [.. table.Skip(2).Select(Cells)];
    }

    /// <summary>The names in backticks in <paramref name="cell"/> that look like column names or tokens: lowercase and underscores.</summary>
    public static IReadOnlyList<string> Names(string cell) =>
        [.. Backticked().Matches(cell).Select(match => match.Groups[1].Value)];

    private static string[] Cells(string row) =>
        [.. row.Trim().Trim('|').Split('|').Select(cell => cell.Trim())];

    [GeneratedRegex("`([a-z][a-z0-9_]*)`")]
    private static partial Regex Backticked();
}
