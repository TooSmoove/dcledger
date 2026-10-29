using System.Text;
using DcLedger.Domain;
using DcLedger.Publishing.Ics;
using Shouldly;
using Xunit;

namespace DcLedger.Publishing.Tests;

public sealed class PinnedTimeZoneTests
{
    private const string Empty = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nEND:VCALENDAR\r\n";

    [Fact]
    public void The_embedded_block_is_the_one_spike_s6_checked()
    {
        Sha256Hash.Compute(Encoding.UTF8.GetBytes(PinnedTimeZone.Block)).Value.ShouldBe(PinnedTimeZone.Sha256);
        PinnedTimeZone.Block.StartsWith("BEGIN:VTIMEZONE\r\nTZID:America/New_York\r\n", StringComparison.Ordinal).ShouldBeTrue();
        PinnedTimeZone.Block.EndsWith("END:VTIMEZONE\r\n", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public void Without_events_the_block_goes_before_end_vcalendar()
    {
        PinnedTimeZone.InsertInto(Empty)
            .ShouldBe("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + PinnedTimeZone.Block + "END:VCALENDAR\r\n");
    }

    [Fact]
    public void The_block_goes_before_the_first_event_line_not_into_text_that_mentions_it()
    {
        const string ics = "BEGIN:VCALENDAR\r\nX-WR-CALNAME:BEGIN:VEVENT\r\nBEGIN:VEVENT\r\nUID:a\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        PinnedTimeZone.InsertInto(ics).ShouldBe(
            "BEGIN:VCALENDAR\r\nX-WR-CALNAME:BEGIN:VEVENT\r\n" + PinnedTimeZone.Block + "BEGIN:VEVENT\r\nUID:a\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
    }

    [Fact]
    public void A_calendar_that_already_has_a_time_zone_is_refused()
    {
        var withZone = PinnedTimeZone.InsertInto(Empty);

        Should.Throw<InvalidOperationException>(() => PinnedTimeZone.InsertInto(withZone));
    }
}
