using System.Text;

namespace DcLedger.Cli.Tests;

/// <summary>
/// Legistar's recorded API answers for every recorded client (tests/fixtures/legistar/{client}), plus meeting pages on
/// <c>{client}.legistar.com</c> that link their agenda the way InSite does and agendas that are a few PDF bytes named
/// after the meeting. Nothing on <c>{client}.legistar1.com</c> is served: its robots.txt forbids it. Used as a
/// <c>FakeFetcher</c> fallback.
/// </summary>
internal static class RecordedLegistar
{
    private static readonly string[] Clients = ["guilford", "winston-salem"];

    public static (byte[] Content, string? MediaType)? Respond(Uri uri)
    {
        var site = Clients.FirstOrDefault(client => uri.Host == $"{client}.legistar.com");
        if (site is not null)
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

        // /v1/{client}/events and /v1/{client}/events/{id}/eventitems
        var segments = uri.AbsolutePath.Split('/');
        if (uri.Host != "webapi.legistar.com" || segments.Length < 4 || segments[1] != "v1" || !Clients.Contains(segments[2]))
        {
            return null;
        }

        var file = segments[^1] switch
        {
            "events" => "events.json",
            "eventitems" => $"eventitems-{segments[^2]}.json",
            _ => null,
        };
        var path = file is null ? null : DclRunner.RepositoryPath("tests", "fixtures", "legistar", segments[2], file);
        return path is not null && File.Exists(path) ? (File.ReadAllBytes(path), "application/json") : null;
    }
}
