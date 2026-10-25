using System.Globalization;
using System.Security.Cryptography;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Shouldly;
using Xunit;

namespace Spikes.ICal;

/// <summary>Spike S6: pinned VTIMEZONE + DST correctness on Windows and Linux.</summary>
public class VTimeZoneSpikeTests
{
    private const string PinnedSha256 = "0c5fc3087bfde95550c1936bbe2d9bd1098a4f93010f7e96bc1830ec60032136";
    private static readonly string PinnedPath = Path.Combine(AppContext.BaseDirectory, "america-new_york.vtimezone.ics");
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void Pinned_block_bytes_are_unchanged()
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(PinnedPath)));
        hash.ShouldBe(PinnedSha256, "git must not normalize line endings in *.ics (see .gitattributes)");
    }

    [Theory]
    [InlineData(2026, 10, 15, "2026-10-15T21:30:00Z")]
    [InlineData(2026, 11, 5, "2026-11-05T22:30:00Z")]
    public void Eastern_wall_time_converts_to_expected_utc(int year, int month, int day, string expected)
    {
        var local = new DateTime(year, month, day, 17, 30, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, Eastern);
        utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture).ShouldBe(expected);
    }

    [Fact]
    public void Gap_and_overlap_wall_times_are_detectable()
    {
        Eastern.IsInvalidTime(new DateTime(2027, 3, 14, 2, 30, 0)).ShouldBeTrue();
        Eastern.IsAmbiguousTime(new DateTime(2026, 11, 1, 1, 30, 0)).ShouldBeTrue();
        Findings.Write("S6", $"TimeZoneInfo id={Eastern.Id} display={Eastern.DisplayName}; gap/overlap detection OK");
    }

    [Fact]
    public void Serialized_calendar_carries_exactly_one_pinned_vtimezone()
    {
        var calendar = new Calendar();
        calendar.Events.Add(new CalendarEvent
        {
            Uid = "spike-guilford-boc-20261105",
            Summary = "Board of Commissioners: test, with comma; and semicolon\nnewline",
            DtStart = new CalDateTime(2026, 11, 5, 17, 30, 0, "America/New_York"),
            DtEnd = new CalDateTime(2026, 11, 5, 19, 30, 0, "America/New_York"),
            Sequence = 1,
            Status = EventStatus.Cancelled,
        });

        var raw = new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;
        var autoTimeZones = Count(raw, "BEGIN:VTIMEZONE");
        Findings.Write("S6", $"Ical.Net 5.2.3 emitted {autoTimeZones} VTIMEZONE block(s) on its own");

        var pinned = File.ReadAllText(PinnedPath);
        var ics = autoTimeZones == 0 ? Inject(raw, pinned) : raw;

        Count(ics, "BEGIN:VTIMEZONE").ShouldBe(1);
        ics.ShouldContain(pinned);
        ics.ShouldContain("DTSTART;TZID=America/New_York:20261105T173000");
        ics.ShouldContain("STATUS:CANCELLED");
        ics.ShouldContain("SEQUENCE:1");
        ics.ShouldContain(@"test\, with comma\; and semicolon\nnewline");
        ics.Replace("\r\n", string.Empty).ShouldNotContain("\n");

        var parsed = Calendar.Load(ics);
        parsed.ShouldNotBeNull();
        parsed.TimeZones.Count.ShouldBe(1);
        parsed.Events.Count.ShouldBe(1);
        Findings.Write("S6", "injection before first VEVENT round-trips through Calendar.Load");
    }

    private static string Inject(string ics, string vtimezone)
    {
        var at = ics.IndexOf("BEGIN:VEVENT", StringComparison.Ordinal);
        at.ShouldBeGreaterThan(0);
        return ics.Insert(at, vtimezone);
    }

    private static int Count(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
