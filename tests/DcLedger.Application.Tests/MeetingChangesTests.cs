using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

public sealed class MeetingChangesTests
{
    private static readonly MeetingSighting Seen = new()
    {
        Date = new DateOnly(2026, 11, 5),
        Time = new TimeOnly(17, 30),
        AgendaPublished = true,
    };

    [Fact]
    public void Same_sighting_is_no_change()
    {
        MeetingChanges.Between(Seen, Seen with { }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(2026, 11, 6, 17, 30)]
    [InlineData(2026, 11, 5, 18, 0)]
    public void New_date_or_time_is_a_reschedule(int year, int month, int day, int hour, int minute)
    {
        var moved = Seen with { Date = new DateOnly(year, month, day), Time = new TimeOnly(hour, minute) };

        MeetingChanges.Between(Seen, moved).ShouldHaveSingleItem().ShouldBe(MeetingChange.Rescheduled);
    }

    [Fact]
    public void Losing_the_start_time_is_a_reschedule()
    {
        MeetingChanges.Between(Seen, Seen with { Time = null }).ShouldHaveSingleItem().ShouldBe(MeetingChange.Rescheduled);
    }

    [Fact]
    public void Cancellation_hints_and_agendas_are_noticed_both_ways()
    {
        var off = Seen with { PossiblyCancelled = true, AgendaPublished = false };

        MeetingChanges.Between(Seen, off).ShouldBe(new List<MeetingChange> { MeetingChange.CancellationMentioned, MeetingChange.AgendaWithdrawn });
        MeetingChanges.Between(off, Seen).ShouldBe(new List<MeetingChange> { MeetingChange.CancellationMentionRemoved, MeetingChange.AgendaPublishedAgain });
    }

    [Theory]
    [InlineData(MeetingChange.CancellationMentioned, true, false)]
    [InlineData(MeetingChange.AgendaWithdrawn, true, false)]
    [InlineData(MeetingChange.NoLongerListed, true, false)]
    [InlineData(MeetingChange.Rescheduled, false, false)]
    [InlineData(MeetingChange.CancellationMentionRemoved, false, true)]
    [InlineData(MeetingChange.AgendaPublishedAgain, false, true)]
    [InlineData(MeetingChange.ListedAgain, false, true)]
    public void Changes_are_sorted_into_possible_cancellations_and_reinstatements(MeetingChange change, bool cancellation, bool reinstatement)
    {
        MeetingChanges.SuggestsCancellation([change]).ShouldBe(cancellation);
        MeetingChanges.SuggestsReinstatement([change]).ShouldBe(reinstatement);
    }

    [Fact]
    public void Every_change_has_a_distinct_token()
    {
        var tokens = Enum.GetValues<MeetingChange>().Select(MeetingChanges.Token).ToList();

        tokens.ShouldBeUnique();
        foreach (var token in tokens)
        {
            token.ShouldMatch("^[a-z]+(-[a-z]+)*$");
        }
    }

    [Fact]
    public void Sighting_keeps_only_schedule_facts_from_the_source()
    {
        var meeting = new SourceMeeting
        {
            Key = new MeetingKey(DcLedger.Domain.Platform.Legistar, "guilford", "1600"),
            Body = "Board of Commissioners",
            Date = new DateOnly(2026, 11, 5),
            Time = new TimeOnly(17, 30),
            PossiblyCancelled = true,
            Items = [new SourceItem { ItemId = "1", Title = "Private title" }],
        };

        MeetingSighting.Of(meeting).ShouldBe(new MeetingSighting { Date = meeting.Date, Time = meeting.Time, PossiblyCancelled = true, AgendaPublished = false });
    }
}
