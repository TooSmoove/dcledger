using System.Text;
using System.Text.RegularExpressions;

namespace DcLedger.Infrastructure.Fetching;

/// <summary>
/// The rules in one robots.txt that apply to our product token, evaluated per RFC 9309: the most specific group
/// (our token, else <c>*</c>), longest matching rule wins, <c>Allow</c> wins ties, <c>*</c> and <c>$</c> supported.
/// The file is untrusted input: patterns become non-backtracking regexes, and a match that still times out disallows.
/// </summary>
internal sealed class RobotsTxt
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    private readonly List<Rule> _rules;

    private RobotsTxt(List<Rule> rules) => _rules = rules;

    /// <summary>No restrictions: robots.txt is missing (HTTP 4xx) or names nothing for us.</summary>
    public static RobotsTxt AllowAll { get; } = new([]);

    /// <summary>Everything disallowed: robots.txt was unreachable (HTTP 5xx or a network failure).</summary>
    public static RobotsTxt DisallowAll { get; } = new([new Rule(Allow: false, "/", Matcher("/"))]);

    /// <summary>Parses <paramref name="content"/> and keeps the rules for <paramref name="productToken"/>.</summary>
    public static RobotsTxt Parse(string content, string productToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(productToken);

        var ours = new List<Rule>();
        var anyone = new List<Rule>();
        var currentAgents = new List<string>();
        var lastWasAgent = false;
        var hasOurGroup = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (key.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (!lastWasAgent)
                {
                    currentAgents.Clear();
                }

                currentAgents.Add(value);
                hasOurGroup |= value.Equals(productToken, StringComparison.OrdinalIgnoreCase);
                lastWasAgent = true;
                continue;
            }

            lastWasAgent = false;
            var allow = key.Equals("allow", StringComparison.OrdinalIgnoreCase);
            if (!allow && !key.Equals("disallow", StringComparison.OrdinalIgnoreCase))
            {
                continue; // sitemap, crawl-delay and unknown keys don't affect access
            }

            if (value.Length == 0 || currentAgents.Count == 0)
            {
                continue; // an empty pattern matches nothing
            }

            var rule = new Rule(allow, value, Matcher(value));
            if (currentAgents.Exists(agent => agent.Equals(productToken, StringComparison.OrdinalIgnoreCase)))
            {
                ours.Add(rule);
            }

            if (currentAgents.Contains("*", StringComparer.Ordinal))
            {
                anyone.Add(rule);
            }
        }

        return new RobotsTxt(hasOurGroup ? ours : anyone);
    }

    /// <summary>True when <paramref name="uri"/> may be fetched. robots.txt itself is always allowed.</summary>
    public bool IsAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var path = uri.PathAndQuery;
        if (path.Equals("/robots.txt", StringComparison.Ordinal))
        {
            return true;
        }

        Rule? best = null;
        foreach (var rule in _rules)
        {
            bool matches;
            try
            {
                matches = rule.Matcher.IsMatch(path);
            }
            catch (RegexMatchTimeoutException)
            {
                return false; // can't tell, so don't fetch
            }

            if (matches
                && (best is null
                    || rule.Pattern.Length > best.Pattern.Length
                    || (rule.Pattern.Length == best.Pattern.Length && rule.Allow && !best.Allow)))
            {
                best = rule;
            }
        }

        return best?.Allow ?? true;
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#', StringComparison.Ordinal);
        return hash >= 0 ? line[..hash] : line;
    }

    private static Regex Matcher(string pattern)
    {
        var anchored = pattern.EndsWith('$');
        var body = anchored ? pattern[..^1] : pattern;
        var regex = new StringBuilder("^");
        var parts = body.Split('*');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                regex.Append(".*");
            }

            regex.Append(Regex.Escape(parts[i]));
        }

        if (anchored)
        {
            regex.Append('$');
        }

        return new Regex(regex.ToString(), RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.Singleline, PatternTimeout);
    }

    private sealed record Rule(bool Allow, string Pattern, Regex Matcher);
}
