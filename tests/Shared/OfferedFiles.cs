// Reads the files a review issue offers to paste, compiled into the Infrastructure and CLI test projects (linked from
// their project files), so the rendered-YAML test and the end-to-end smoke test read issues the same way.
using System.Text.RegularExpressions;

namespace DcLedger.Testing;

/// <summary>The <c>data/…yaml</c> files in a review issue's body, as a reviewer would copy them.</summary>
internal static partial class OfferedFiles
{
    /// <summary>Each offered file's repository path (for example <c>data/events/x.yaml</c>) and YAML.</summary>
    public static Dictionary<string, string> Parse(string issueBody) =>
        OfferedFile().Matches(issueBody).ToDictionary(match => match.Groups["path"].Value, match => match.Groups["yaml"].Value);

    /// <summary>Fills the empty <c>title</c> the reviewer must write; throws if the YAML has no such placeholder.</summary>
    public static string WithTitle(string yaml, string title)
    {
        var filled = EmptyTitle().Replace(yaml, $"title: \"{title}\"", 1);
        return filled != yaml ? filled : throw new InvalidOperationException("The offered YAML has no empty title to fill.");
    }

    [GeneratedRegex("`(?<path>data/[a-z]+/[a-z0-9-]+\\.yaml)`\\n\\n```yaml\\n(?<yaml>.*?)```", RegexOptions.Singleline)]
    private static partial Regex OfferedFile();

    [GeneratedRegex("^title: \"\".*$", RegexOptions.Multiline)]
    private static partial Regex EmptyTitle();
}
