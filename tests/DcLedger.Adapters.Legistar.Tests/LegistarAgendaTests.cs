using System.Text;
using DcLedger.Application;
using Shouldly;
using Xunit;

namespace DcLedger.Adapters.Legistar.Tests;

/// <summary>
/// Agendas come through the meeting's page on {client}.legistar.com. The API's file host, guilford.legistar1.com, has
/// "User-agent: * / Disallow: /" in its robots.txt (checked Oct 29, 2026, issue #19), so it is never asked.
/// </summary>
public sealed class LegistarAgendaTests
{
    private const string PagePath = "/MeetingDetail.aspx";
    private const string AgendaPath = "/View.ashx";
    private const string AgendaGuid = "50757AC6-9ADB-4840-A8C9-09E7E8BAD1A3";
    private const string Group = "CC3BAA2D-CF4F-4E63-9ADD-2F9E8DCCB38C";

    private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.7 agenda");

    private readonly FixtureFetcher _fetcher = new("guilford");

    public LegistarAgendaTests()
    {
        // The accessible copy (M=AADA) comes first on the real page; only M=A is the agenda.
        Page($"""<a href="View.ashx?M=AADA&amp;ID=1370331&amp;GUID={AgendaGuid}&amp;G={Group}">Accessible</a> <a href="View.ashx?M=A&amp;ID=1370331&amp;GUID={AgendaGuid}&amp;G={Group}">Agenda</a>""");
        _fetcher.Overrides[AgendaPath] = uri => FixtureFetcher.Json(uri, Pdf, "application/pdf");
    }

    [Fact]
    public async Task Agenda_is_read_through_the_meeting_page_never_the_file_host()
    {
        var meeting = await Hearing();

        var agenda = await Adapter().GetAgendaAsync(meeting, Ct);

        agenda.Content.ToArray().ShouldBe(Pdf);
        agenda.FinalUri.ShouldBe(new Uri($"https://guilford.legistar.com/View.ashx?M=A&ID=1370331&GUID={AgendaGuid}&G={Group}"));
        var asked = _fetcher.Requests.TakeLast(2).ToList();
        asked[0].ShouldBe(meeting.MeetingPageUri);
        asked[1].ShouldBe(agenda.FinalUri);
        _fetcher.Requests.ShouldNotContain(uri => uri.Host == "guilford.legistar1.com");
    }

    [Fact]
    public async Task Link_without_a_group_is_rebuilt_without_one()
    {
        Page($"""<a href="View.ashx?M=A&amp;ID=42&amp;GUID={AgendaGuid}">Agenda</a>""");

        var agenda = await Adapter().GetAgendaAsync(await Hearing(), Ct);

        agenda.FinalUri.ShouldBe(new Uri($"https://guilford.legistar.com/View.ashx?M=A&ID=42&GUID={AgendaGuid}"));
    }

    [Theory]
    [InlineData("<p>No agenda yet.</p>")]
    [InlineData("<a href=\"https://evil.example/View.ashx?M=A&amp;ID=1&amp;GUID=50757AC6-9ADB-4840-A8C9-09E7E8BAD1A3\">x</a>")]
    [InlineData("<a href=\"View.ashx?M=A&amp;ID=1&amp;GUID=50757AC6-9ADB-4840-A8C9-09E7E8BAD1A3&amp;next=javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"View.ashx?M=A&amp;ID=1x&amp;GUID=not-a-guid\">x</a>")]
    [InlineData("<a href=\"View.ashx?M=A&amp;ID=1&amp;GUID=50757AC6-9ADB-4840-A8C9-09E7E8BAD1A3\">a</a><a href=\"View.ashx?M=A&amp;ID=2&amp;GUID=50757AC6-9ADB-4840-A8C9-09E7E8BAD1A3\">b</a>")]
    public async Task Page_without_exactly_one_recognizable_agenda_link_is_a_format_change(string html)
    {
        Page(html);
        var meeting = await Hearing();

        var failure = await Should.ThrowAsync<SourceFailedException>(() => Adapter().GetAgendaAsync(meeting, Ct));

        failure.Failure.ShouldBe(SourceFailure.FormatChanged);
        failure.Uri.ShouldBe(meeting.MeetingPageUri);
        _fetcher.Requests.ShouldNotContain(uri => uri.AbsolutePath == AgendaPath || (uri.Host != "guilford.legistar.com" && uri.Host != "webapi.legistar.com"));
    }

    [Fact]
    public async Task Answer_that_is_not_a_pdf_is_a_format_change()
    {
        _fetcher.Overrides[AgendaPath] = uri => FixtureFetcher.Json(uri, Encoding.UTF8.GetBytes("<html>Object moved</html>"), "text/html");

        var failure = await Should.ThrowAsync<SourceFailedException>(async () => await Adapter().GetAgendaAsync(await Hearing(), Ct));

        failure.Failure.ShouldBe(SourceFailure.FormatChanged);
        failure.Message.ShouldContain("didn't answer with a PDF");
    }

    [Theory]
    [InlineData("https://evil.example/MeetingDetail.aspx?LEGID=1599")]
    [InlineData("http://guilford.legistar.com/MeetingDetail.aspx?LEGID=1599")]
    [InlineData(null)]
    public async Task Meeting_page_off_the_client_site_is_refused_without_a_request(string? page)
    {
        var meeting = (await Hearing()) with { MeetingPageUri = page is null ? null : new Uri(page) };
        var before = _fetcher.Requests.Count;

        var failure = await Should.ThrowAsync<SourceFailedException>(() => Adapter().GetAgendaAsync(meeting, Ct));

        failure.Failure.ShouldBe(SourceFailure.FormatChanged);
        _fetcher.Requests.Count.ShouldBe(before);
    }

    [Fact]
    public async Task Refusals_and_outages_keep_their_meaning()
    {
        _fetcher.Overrides[PagePath] = uri => throw new FetchFailedException(uri, FetchFailure.RobotsDisallowed, "robots.txt disallows it");
        var refused = await Should.ThrowAsync<SourceFailedException>(async () => await Adapter().GetAgendaAsync(await Hearing(), Ct));

        _fetcher.Overrides[PagePath] = uri => throw new FetchFailedException(uri, FetchFailure.HttpStatus, "HTTP 503", 503);
        var down = await Should.ThrowAsync<SourceFailedException>(async () => await Adapter().GetAgendaAsync(await Hearing(), Ct));

        refused.Failure.ShouldBe(SourceFailure.Rejected);
        down.Failure.ShouldBe(SourceFailure.Unavailable);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LegistarAdapter Adapter() => new(_fetcher);

    private void Page(string html) =>
        _fetcher.Overrides[PagePath] = uri => FixtureFetcher.Json(uri, Encoding.UTF8.GetBytes(html), "text/html");

    private async Task<SourceMeeting> Hearing() =>
        (await Adapter().GetMeetingsAsync(LegistarAdapterTests.Guilford(), new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 1), Ct))
            .Single(meeting => meeting.Key.MeetingId == "1599");
}
