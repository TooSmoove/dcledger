using System.Text;
using System.Text.RegularExpressions;

namespace DcLedger.Adapters.Legistar;

/// <summary>
/// Finds the agenda link on a Legistar meeting page (<c>MeetingDetail.aspx</c>). Not a general HTML reader: the page is
/// untrusted, so only one link shape is recognized, and the address is rebuilt from its validated parts on the page's
/// own host rather than taken as written.
/// </summary>
internal static partial class LegistarMeetingPage
{
    private const int TimeoutMilliseconds = 1000;

    /// <summary>
    /// The page's one agenda link, as <c>https://{page host}/View.ashx?M=A&amp;ID=…&amp;GUID=…[&amp;G=…]</c>. Throws a
    /// FormatChanged <see cref="DcLedger.Application.SourceFailedException"/> when there is none or more than one.
    /// </summary>
    public static Uri AgendaLink(Uri page, ReadOnlyMemory<byte> html)
    {
        ArgumentNullException.ThrowIfNull(page);
        List<(string Id, string Guid, string G)> links;
        try
        {
            links = [.. AgendaHref().Matches(Encoding.UTF8.GetString(html.Span))
                .Select(match => (match.Groups["id"].Value, match.Groups["guid"].Value, match.Groups["g"].Value))
                .Distinct()];
        }
        catch (RegexMatchTimeoutException exception)
        {
            throw LegistarMapping.FormatChanged(page, "took too long to search for its agenda link.", exception);
        }

        if (links.Count != 1)
        {
            throw LegistarMapping.FormatChanged(page, links.Count == 0
                ? "doesn't link an agenda (View.ashx?M=A) any more."
                : "links more than one agenda.");
        }

        var (id, guid, g) = links[0];
        var group = g.Length > 0 ? $"&G={g}" : string.Empty;
        return new Uri($"https://{page.Host}/View.ashx?M=A&ID={id}&GUID={guid}{group}");
    }

    // As InSite writes it: href="View.ashx?M=A&amp;ID=1370331&amp;GUID=50757AC6-…&amp;G=CC3BAA2D-…". M=AADA (the
    // accessible copy) doesn't match because "&amp;" must follow "M=A".
    [GeneratedRegex(
        @"href=""View\.ashx\?M=A&amp;ID=(?<id>[0-9]{1,10})&amp;GUID=(?<guid>[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12})(?:&amp;G=(?<g>[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}))?""",
        RegexOptions.CultureInvariant,
        TimeoutMilliseconds)]
    private static partial Regex AgendaHref();
}
