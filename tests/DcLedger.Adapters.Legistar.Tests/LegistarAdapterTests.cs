using DcLedger.Application;
using DcLedger.Domain;
using Shouldly;
using Xunit;

namespace DcLedger.Adapters.Legistar.Tests;

/// <summary>Contract tests over the recorded Guilford responses (tests/fixtures/legistar/README.md).</summary>
public sealed class LegistarAdapterTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 12, 1);

    private readonly FixtureFetcher _fetcher = new("guilford");

    [Fact]
    public async Task Reads_every_configured_meeting_in_date_and_time_order()
    {
        var meetings = await Read(Guilford());

        string[] expected = ["1628", "1598", "1629", "1630", "1599", "1631"];
        meetings.Select(m => m.Key.MeetingId).ShouldBe(expected);
        meetings.ShouldAllBe(m => m.Key.Platform == Platform.Legistar && m.Key.Client == "guilford");
    }

    [Fact]
    public async Task Maps_a_regular_meeting()
    {
        var meeting = (await Read(Guilford())).Single(m => m.Key.MeetingId == "1599");

        meeting.Body.ShouldBe("Board of Commissioners");
        meeting.Date.ShouldBe(new DateOnly(2026, 10, 15));
        meeting.Time.ShouldBe(new TimeOnly(17, 30));
        meeting.StartsAt.ShouldBe(new DateTimeOffset(2026, 10, 15, 17, 30, 0, TimeSpan.FromHours(-4)));
        meeting.StartsAt!.Value.Offset.ShouldBe(TimeSpan.FromHours(-4));
        meeting.AgendaUri.ShouldBe(new Uri("https://guilford.legistar1.com/guilford/meetings/2026/10/1599_A_Board_of_Commissioners_26-10-15_Meeting_Agenda.pdf"));
        meeting.MeetingPageUri!.Host.ShouldBe("guilford.legistar.com");
        meeting.PossiblyCancelled.ShouldBeFalse();
        meeting.Items.Count.ShouldBe(6);
    }

    [Fact]
    public async Task Body_name_with_stray_whitespace_still_matches_and_is_trimmed()
    {
        var meeting = (await Read(Guilford())).Single(m => m.Key.MeetingId == "1630");

        meeting.Body.ShouldBe("Board of Commissioners Work Session");
    }

    [Fact]
    public async Task Classifier_finds_the_one_data_center_hearing()
    {
        var meetings = await Read(Guilford());

        var hits = meetings
            .SelectMany(m => m.Items.Select(item => (m.Key.MeetingId, Item: item)))
            .Where(hit => MeetingItemClassifier.Classify(hit.Item.ClassifierText).IsCandidate)
            .ToList();

        var hit = hits.ShouldHaveSingleItem();
        hit.MeetingId.ShouldBe("1599");
        hit.Item.ItemId.ShouldBe("38603");
        hit.Item.AgendaNumber.ShouldBe("A.");
        hit.Item.CaseNumber.ShouldBe("2026-439");
        hit.Item.MatterType.ShouldBe("Public Hearing");
    }

    [Fact]
    public async Task Items_are_fetched_only_for_configured_bodies()
    {
        string[] regularMeetingsOnly = ["Board of Commissioners"];

        await Read(Guilford(regularMeetingsOnly));

        string[] expected =
        [
            "/v1/guilford/events",
            "/v1/guilford/events/1598/eventitems",
            "/v1/guilford/events/1599/eventitems",
            "/v1/guilford/events/1629/eventitems",
            "/v1/guilford/events/1631/eventitems",
        ];
        _fetcher.Requests.Select(uri => uri.AbsolutePath).ShouldBe(expected);
    }

    [Fact]
    public async Task Events_are_requested_for_the_window_only()
    {
        await Read(Guilford());

        var events = _fetcher.Requests[0];
        events.Host.ShouldBe("webapi.legistar.com");
        Uri.UnescapeDataString(events.Query).ShouldBe("?$filter=EventDate ge datetime'2026-10-01' and EventDate lt datetime'2026-12-01'");
    }

    private Task<IReadOnlyList<SourceMeeting>> Read(Jurisdiction jurisdiction) =>
        new LegistarAdapter(_fetcher).GetMeetingsAsync(jurisdiction, From, To, TestContext.Current.CancellationToken);

    /// <summary>Guilford as data/jurisdictions/guilford.yaml configures it.</summary>
    internal static Jurisdiction Guilford(IReadOnlyList<string>? bodies = null) => new()
    {
        Id = "guilford",
        Name = "Guilford County",
        Platform = Platform.Legistar,
        LegistarClient = "guilford",
        Bodies = bodies ?? ["Board of Commissioners", "Board of Commissioners - Work Session", "Board of Commissioners Work Session"],
    };
}
