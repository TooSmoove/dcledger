using DcLedger.Application;
using DcLedger.Domain;
using Shouldly;
using Xunit;

namespace DcLedger.Adapters.Legistar.Tests;

/// <summary>
/// Contract tests over the recorded Winston-Salem responses (tests/fixtures/legistar/README.md): the second Legistar
/// jurisdiction runs on configuration alone (issue #26).
/// </summary>
public sealed class WinstonSalemAdapterTests
{
    private static readonly DateOnly From = new(2026, 4, 27);
    private static readonly DateOnly To = new(2027, 1, 1);

    private readonly FixtureFetcher _fetcher = new("winston-salem");

    [Fact]
    public async Task Reads_only_full_council_meetings_in_date_order()
    {
        var meetings = await Read();

        string[] expected = ["2804", "2809", "2810", "2815", "2838", "2816", "2821", "2822", "2827", "2828", "2842", "2843"];
        meetings.Select(m => m.Key.MeetingId).ShouldBe(expected);
        meetings.ShouldAllBe(m => m.Key.Platform == Platform.Legistar && m.Key.Client == "winston-salem");
        string[] bodies = ["City Council", "Special Meeting of the City Council"]; // no emergency meeting in the window
        meetings.Select(m => m.Body).Distinct().Order(StringComparer.Ordinal).ShouldBe(bodies);
    }

    [Fact]
    public async Task Committee_meetings_are_listed_by_the_api_but_their_items_are_never_fetched()
    {
        await Read();

        var itemRequests = _fetcher.Requests.Skip(1).Select(uri => uri.AbsolutePath).ToList();
        itemRequests.Count.ShouldBe(12);
        // Committee of the Whole, Public Safety, General Government (its name ends in a space), Budget Workshop
        string[] committeeMeetings = ["2805", "2811", "2812", "2833"];
        foreach (var id in committeeMeetings)
        {
            itemRequests.ShouldNotContain($"/v1/winston-salem/events/{id}/eventitems");
        }
    }

    [Fact]
    public async Task Maps_a_regular_meeting_on_daylight_time()
    {
        var meeting = (await Read()).Single(m => m.Key.MeetingId == "2821");

        meeting.Body.ShouldBe("City Council");
        meeting.Date.ShouldBe(new DateOnly(2026, 8, 17));
        meeting.Time.ShouldBe(new TimeOnly(18, 0));
        meeting.StartsAt.ShouldBe(new DateTimeOffset(2026, 8, 17, 18, 0, 0, TimeSpan.FromHours(-4)));
        meeting.AgendaUri!.Host.ShouldBe("winston-salem.legistar1.com");
        meeting.MeetingPageUri.ShouldBe(new Uri("https://winston-salem.legistar.com/MeetingDetail.aspx?LEGID=2821&GID=657&G=8C57BC0C-9DFF-4123-B207-922118F33F71"));
        meeting.PossiblyCancelled.ShouldBeFalse();
    }

    [Fact]
    public async Task Meeting_after_daylight_saving_ends_gets_the_standard_offset()
    {
        var meeting = (await Read()).Single(m => m.Key.MeetingId == "2843");

        meeting.Date.ShouldBe(new DateOnly(2026, 11, 2));
        meeting.StartsAt.ShouldBe(new DateTimeOffset(2026, 11, 2, 18, 0, 0, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public async Task Special_meeting_is_read_like_a_regular_one()
    {
        var meeting = (await Read()).Single(m => m.Key.MeetingId == "2838");

        meeting.Body.ShouldBe("Special Meeting of the City Council");
        meeting.StartsAt.ShouldBe(new DateTimeOffset(2026, 6, 22, 16, 0, 0, TimeSpan.FromHours(-4)));
        meeting.AgendaUri.ShouldNotBeNull();
    }

    [Fact]
    public async Task Recorded_cancellation_is_flagged_and_has_no_agenda()
    {
        var meeting = (await Read()).Single(m => m.Key.MeetingId == "2816");

        meeting.Date.ShouldBe(new DateOnly(2026, 7, 6));
        meeting.PossiblyCancelled.ShouldBeTrue();
        meeting.AgendaUri.ShouldBeNull();
        meeting.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Classifier_finds_the_one_data_center_ordinance()
    {
        var meetings = await Read();

        var hit = meetings
            .SelectMany(m => m.Items.Select(item => (m.Key.MeetingId, Item: item)))
            .Where(hit => MeetingItemClassifier.Classify(hit.Item.ClassifierText).IsCandidate)
            .ShouldHaveSingleItem();
        hit.MeetingId.ShouldBe("2821");
        hit.Item.AgendaNumber.ShouldBe("G-10.");
        hit.Item.CaseNumber.ShouldBe("26-0310");
        hit.Item.MatterType.ShouldBe("Ordinance");
    }

    private Task<IReadOnlyList<SourceMeeting>> Read() =>
        new LegistarAdapter(_fetcher).GetMeetingsAsync(WinstonSalem(), From, To, TestContext.Current.CancellationToken);

    /// <summary>Winston-Salem as data/jurisdictions/winston-salem.yaml configures it (the CLI tests load the file itself).</summary>
    internal static Jurisdiction WinstonSalem() => new()
    {
        Id = "winston-salem",
        Name = "Winston-Salem",
        Platform = Platform.Legistar,
        LegistarClient = "winston-salem",
        Bodies = ["City Council", "Special Meeting of the City Council", "Emergency Meeting of the City Council"],
    };
}
