using System.Text;
using DcLedger.Application;
using DcLedger.Domain;
using Shouldly;
using Xunit;

namespace DcLedger.Adapters.Legistar.Tests;

/// <summary>Cases the recording doesn't contain (tests/fixtures/legistar/derived) and responses that must fail.</summary>
public sealed class LegistarEdgeCaseTests
{
    private const string EventsPath = "/v1/guilford/events";

    [Fact]
    public async Task Missing_time_leaves_the_start_for_a_reviewer()
    {
        var meeting = await Derived("9001");

        meeting.Date.ShouldBe(new DateOnly(2026, 11, 5));
        meeting.Time.ShouldBeNull();
        meeting.StartsAt.ShouldBeNull();
    }

    [Fact]
    public async Task Comment_mentioning_a_cancellation_flags_the_meeting_for_a_human()
    {
        (await Derived("9002")).PossiblyCancelled.ShouldBeTrue();
        (await Derived("9001")).PossiblyCancelled.ShouldBeFalse();
    }

    [Fact]
    public async Task Wall_time_skipped_by_daylight_saving_is_not_guessed()
    {
        var meeting = await Derived("9003");

        meeting.Time.ShouldBe(new TimeOnly(2, 30));
        meeting.StartsAt.ShouldBeNull();
    }

    [Fact]
    public async Task Draft_without_an_agenda_file_has_no_agenda_uri()
    {
        (await Derived("9006")).AgendaUri.ShouldBeNull();
    }

    [Fact]
    public async Task Unconfigured_bodies_and_dates_outside_the_window_are_skipped_without_requests()
    {
        var fetcher = new FixtureFetcher("derived");

        var meetings = await Read(fetcher);

        string[] expected = ["9003", "9001", "9006", "9002"];
        meetings.Select(m => m.Key.MeetingId).ShouldBe(expected);
        fetcher.Requests.ShouldNotContain(uri => uri.AbsolutePath.Contains("/9004/", StringComparison.Ordinal));
        fetcher.Requests.ShouldNotContain(uri => uri.AbsolutePath.Contains("/9005/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""[{"EventId":"1","EventBodyName":"Board of Commissioners","EventDate":"2026-11-05T00:00:00"}]""")]
    [InlineData("""[{"EventId":1,"EventDate":"2026-11-05T00:00:00"}]""")]
    [InlineData("""[{"EventId":1,"EventBodyName":null,"EventDate":"2026-11-05T00:00:00"}]""")]
    [InlineData("""[{"EventId":1,"EventBodyName":"Board of Commissioners","EventDate":"next Thursday"}]""")]
    [InlineData("""[{"EventId":1,"EventBodyName":"Board of Commissioners","EventDate":"2026-11-05T00:00:00","EventTime":"TBD"}]""")]
    [InlineData("""[{"EventId":1,"EventBodyName":"Board of Commissioners","EventDate":"2026-11-05T00:00:00","EventAgendaFile":"javascript:alert(1)"}]""")]
    [InlineData("""{"EventId":1}""")]
    [InlineData("""[null]""")]
    [InlineData("null")]
    [InlineData("<html>maintenance</html>")]
    public async Task Unexpected_events_response_is_a_format_change(string body)
    {
        var fetcher = new FixtureFetcher("derived");
        fetcher.Overrides[EventsPath] = uri => FixtureFetcher.Json(uri, Encoding.UTF8.GetBytes(body));

        var failure = await Should.ThrowAsync<SourceFailedException>(() => Read(fetcher));

        failure.Failure.ShouldBe(SourceFailure.FormatChanged);
        failure.Uri!.AbsolutePath.ShouldStartWith("/v1/guilford/");
    }

    [Fact]
    public async Task Html_instead_of_json_is_a_format_change()
    {
        var fetcher = new FixtureFetcher("derived");
        fetcher.Overrides[EventsPath] = uri => FixtureFetcher.Json(uri, Encoding.UTF8.GetBytes("<html></html>"), "text/html");

        var failure = await Should.ThrowAsync<SourceFailedException>(() => Read(fetcher));

        failure.Failure.ShouldBe(SourceFailure.FormatChanged);
    }

    [Fact]
    public async Task Full_page_of_meetings_is_a_format_change_rather_than_silently_truncated()
    {
        var page = "[" + string.Join(",", Enumerable.Range(1, LegistarAdapter.PageLimit)
            .Select(id => $$"""{"EventId":{{id}},"EventBodyName":"Other","EventDate":"2026-11-05T00:00:00"}""")) + "]";
        var fetcher = new FixtureFetcher("derived");
        fetcher.Overrides[EventsPath] = uri => FixtureFetcher.Json(uri, Encoding.UTF8.GetBytes(page));

        var failure = await Should.ThrowAsync<SourceFailedException>(() => Read(fetcher));

        failure.Failure.ShouldBe(SourceFailure.FormatChanged);
    }

    [Theory]
    [InlineData(FetchFailure.RobotsDisallowed, null, SourceFailure.Rejected)]
    [InlineData(FetchFailure.HttpStatus, 403, SourceFailure.Rejected)]
    [InlineData(FetchFailure.HttpStatus, 404, SourceFailure.Rejected)]
    [InlineData(FetchFailure.HttpStatus, 429, SourceFailure.Unavailable)]
    [InlineData(FetchFailure.HttpStatus, 503, SourceFailure.Unavailable)]
    [InlineData(FetchFailure.Timeout, null, SourceFailure.Unavailable)]
    [InlineData(FetchFailure.Network, null, SourceFailure.Unavailable)]
    [InlineData(FetchFailure.Redirect, null, SourceFailure.Unavailable)]
    [InlineData(FetchFailure.TooLarge, null, SourceFailure.FormatChanged)]
    public async Task Fetch_failures_become_source_failures(FetchFailure fetchFailure, int? status, SourceFailure expected)
    {
        var fetcher = new FixtureFetcher("derived");
        fetcher.Overrides["/v1/guilford/events/9001/eventitems"] = uri => throw new FetchFailedException(uri, fetchFailure, "failed", status);

        var failure = await Should.ThrowAsync<SourceFailedException>(() => Read(fetcher));

        failure.Failure.ShouldBe(expected);
        failure.Uri!.AbsolutePath.ShouldBe("/v1/guilford/events/9001/eventitems");
        failure.InnerException.ShouldBeOfType<FetchFailedException>();
    }

    [Fact]
    public async Task Non_legistar_jurisdiction_and_empty_window_are_caller_errors()
    {
        var adapter = new LegistarAdapter(new FixtureFetcher("derived"));
        var manual = LegistarAdapterTests.Guilford() with { Platform = Platform.Manual, LegistarClient = null };
        var day = new DateOnly(2026, 11, 5);

        await Should.ThrowAsync<ArgumentException>(() => adapter.GetMeetingsAsync(manual, day, day.AddDays(1), Ct));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => adapter.GetMeetingsAsync(LegistarAdapterTests.Guilford(), day, day, Ct));
    }

    private static async Task<SourceMeeting> Derived(string meetingId) =>
        (await Read(new FixtureFetcher("derived"))).Single(m => m.Key.MeetingId == meetingId);

    private static Task<IReadOnlyList<SourceMeeting>> Read(FixtureFetcher fetcher) =>
        new LegistarAdapter(fetcher).GetMeetingsAsync(LegistarAdapterTests.Guilford(), new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 1), Ct);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
