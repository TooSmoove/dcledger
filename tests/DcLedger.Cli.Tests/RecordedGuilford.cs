using System.Text;

namespace DcLedger.Cli.Tests;

/// <summary>
/// Legistar's recorded API answers for Guilford (tests/fixtures/legistar/guilford), plus meeting pages that link their
/// agenda the way InSite does and agendas that are a few PDF bytes named after the meeting. Nothing on
/// guilford.legistar1.com is served: its robots.txt forbids it. Used as a <c>FakeFetcher</c> fallback.
/// </summary>
internal static class RecordedGuilford
{
    public static (byte[] Content, string? MediaType)? Respond(Uri uri)
    {
        if (uri.Host == "guilford.legistar.com")
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return uri.AbsolutePath switch
            {
                "/MeetingDetail.aspx" => (Encoding.UTF8.GetBytes(
                    $"<a href=\"View.ashx?M=A&amp;ID=13{query["LEGID"]}&amp;GUID=50757AC6-9ADB-4840-A8C9-09E7E8BAD1A3&amp;G=CC3BAA2D-CF4F-4E63-9ADD-2F9E8DCCB38C\">Agenda</a>"),
                    "text/html"),
                "/View.ashx" => (Encoding.UTF8.GetBytes($"%PDF-1.7 agenda {query["ID"]}"), "application/pdf"),
                _ => null,
            };
        }

        var segments = uri.AbsolutePath.Split('/');
        var file = segments[^1] switch
        {
            "events" => "events.json",
            "eventitems" => $"eventitems-{segments[^2]}.json",
            _ => null,
        };
        var path = file is null ? null : DclRunner.RepositoryPath("tests", "fixtures", "legistar", "guilford", file);
        return uri.Host == "webapi.legistar.com" && path is not null && File.Exists(path) ? (File.ReadAllBytes(path), "application/json") : null;
    }
}
