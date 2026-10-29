using System.Text;
using DcLedger.Application;
using DcLedger.Publishing.Ics;
using DcLedger.Testing;
using Ical.Net.CalendarComponents;
using Shouldly;
using Xunit;
using static VerifyXunit.Verifier;
using IcalCalendar = Ical.Net.Calendar;

namespace DcLedger.Publishing.Tests;

public sealed class RadarCalendarExporterTests
{
    private static readonly DateTime October15Utc = new(2026, 10, 15, 21, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime November5Utc = new(2026, 11, 5, 22, 30, 0, DateTimeKind.Utc);

    // Verify compares text with LF line endings; CRLF is asserted byte for byte below.
    [Fact]
    public Task Radar_golden() => Verify(Ics(ExportPaths.Radar).ReplaceLineEndings("\n"));

    [Fact]
    public Task Jurisdiction_calendar_golden() => Verify(Ics(ExportPaths.JurisdictionRadar(ExportSamples.County.Id)).ReplaceLineEndings("\n"));

    [Fact]
    public void There_is_one_calendar_for_all_and_one_per_jurisdiction()
    {
        Files().Select(file => file.Path).ShouldBe(
        [
            ExportPaths.Radar,
            ExportPaths.JurisdictionRadar(ExportSamples.City.Id),
            ExportPaths.JurisdictionRadar(ExportSamples.County.Id),
        ]);
        Events(Ics(ExportPaths.JurisdictionRadar(ExportSamples.County.Id))).Select(e => e.Uid)
            .ShouldBe([ExportSamples.October15.Uid, ExportSamples.November5.Uid]);
        Events(Ics(ExportPaths.JurisdictionRadar(ExportSamples.City.Id))).Select(e => e.Uid)
            .ShouldBe([ExportSamples.November3.Uid]);
    }

    [Fact]
    public void Lines_end_in_crlf_fit_in_75_octets_and_there_is_no_bom()
    {
        foreach (var file in Files())
        {
            var text = TestFiles.Text(file);
            text.StartsWith("BEGIN:VCALENDAR\r\n", StringComparison.Ordinal).ShouldBeTrue(file.Path);
            text.EndsWith("END:VCALENDAR\r\n", StringComparison.Ordinal).ShouldBeTrue(file.Path);
            text.Replace("\r\n", string.Empty, StringComparison.Ordinal).AsSpan().IndexOfAny('\r', '\n').ShouldBe(-1, file.Path);
            foreach (var line in text.Split("\r\n"))
            {
                Encoding.UTF8.GetByteCount(line).ShouldBeLessThanOrEqualTo(75, line);
            }
        }
    }

    [Fact]
    public void The_pinned_vtimezone_appears_once_before_the_first_event()
    {
        foreach (var file in Files())
        {
            var text = TestFiles.Text(file);
            var block = text.IndexOf(PinnedTimeZone.Block, StringComparison.Ordinal);

            block.ShouldBeGreaterThan(0, file.Path);
            text.IndexOf("BEGIN:VTIMEZONE", block + 1, StringComparison.Ordinal).ShouldBe(-1, file.Path);
            block.ShouldBeLessThan(text.IndexOf("\r\nBEGIN:VEVENT\r\n", StringComparison.Ordinal), file.Path);
        }
    }

    [Fact]
    public void Start_times_use_the_eastern_tzid_and_resolve_across_the_daylight_saving_change()
    {
        var lines = UnfoldedLines(Ics(ExportPaths.Radar));
        lines.ShouldContain("DTSTART;TZID=America/New_York:20261015T173000");
        lines.ShouldContain("DTSTART;TZID=America/New_York:20261103T180000");
        lines.ShouldContain("DTSTART;TZID=America/New_York:20261105T173000");

        var events = Events(Ics(ExportPaths.Radar)).ToDictionary(e => e.Uid!, StringComparer.Ordinal);
        events[ExportSamples.October15.Uid].DtStart!.AsUtc.ShouldBe(October15Utc);
        events[ExportSamples.November5.Uid].DtStart!.AsUtc.ShouldBe(November5Utc);
    }

    [Fact]
    public void A_cancelled_meeting_keeps_its_uid_and_carries_status_sequence_and_text()
    {
        var events = Events(Ics(ExportPaths.Radar)).ToDictionary(e => e.Uid!, StringComparer.Ordinal);

        var cancelled = events[ExportSamples.October15.Uid];
        cancelled.Status.ShouldBe("CANCELLED");
        cancelled.Sequence.ShouldBe(1);
        cancelled.Summary.ShouldBe("Cancelled: " + ExportSamples.October15.Title);

        var scheduled = events[ExportSamples.November5.Uid];
        scheduled.Status.ShouldBeNull();
        scheduled.Sequence.ShouldBe(0);
        scheduled.Summary.ShouldBe(ExportSamples.November5.Title);
    }

    [Fact]
    public void Dtstamp_is_the_newest_document_retrieval_so_unchanged_events_keep_their_bytes()
    {
        var events = Events(Ics(ExportPaths.Radar)).ToDictionary(e => e.Uid!, StringComparer.Ordinal);

        events[ExportSamples.October15.Uid].DtStamp!.AsUtc.ShouldBe(new DateTime(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc));
        events[ExportSamples.November5.Uid].DtStamp!.AsUtc.ShouldBe(new DateTime(2026, 10, 28, 12, 47, 3, DateTimeKind.Utc));
    }

    [Fact]
    public void Text_is_escaped_and_the_description_carries_provenance()
    {
        UnfoldedLines(Ics(ExportPaths.Radar)).ShouldContain(@"SUMMARY:Cancelled: Rezoning hearing\, ""Project Example""\; phase 2");

        var description = Events(Ics(ExportPaths.Radar)).Single(e => e.Uid == ExportSamples.October15.Uid).Description!;
        description.Split('\n').ShouldBe(
        [
            "Fixture County, Board of Commissioners",
            "Case: Z-26-01",
            $"Source: https://news.example.org/2026/10/fixture-county-hearing (retrieved 2026-10-11T05:30:00-04:00, SHA-256 {ExportSamples.NewsStory.Id.Value})",
            $"Source: https://fixture-county.example.org/agendas/1599.pdf (retrieved 2026-10-10T08:00:00-04:00, SHA-256 {ExportSamples.CountyAgenda1599.Id.Value})",
        ]);
    }

    [Fact]
    public void Calendar_properties_are_fixed()
    {
        var radar = UnfoldedLines(Ics(ExportPaths.Radar));
        radar.ShouldContain("VERSION:2.0");
        radar.ShouldContain("PRODID:-//dc-ledger//Hearing Radar//EN");
        radar.ShouldContain("X-WR-CALNAME:dc-ledger Hearing Radar");
        radar.ShouldContain("X-PUBLISHED-TTL:PT6H");

        UnfoldedLines(Ics(ExportPaths.JurisdictionRadar(ExportSamples.County.Id)))
            .ShouldContain("X-WR-CALNAME:dc-ledger Hearing Radar: Fixture County");
    }

    [Fact]
    public void Calendars_without_events_still_hold_the_time_zone_and_parse()
    {
        var model = ExportSamples.Model() with { Events = [] };

        foreach (var file in new RadarCalendarExporter().Export(model))
        {
            var text = TestFiles.Text(file);
            text.Contains(PinnedTimeZone.Block, StringComparison.Ordinal).ShouldBeTrue(file.Path);
            text.Contains("BEGIN:VEVENT", StringComparison.Ordinal).ShouldBeFalse(file.Path);

            var calendar = IcalCalendar.Load(text);
            calendar.ShouldNotBeNull();
            calendar.TimeZones.Count.ShouldBe(1);
            calendar.Events.Count.ShouldBe(0);
        }
    }

    private static IReadOnlyList<ExportFile> Files() => new RadarCalendarExporter().Export(ExportSamples.Model());

    private static string Ics(string path) => TestFiles.Text(TestFiles.Single(Files(), path));

    private static List<CalendarEvent> Events(string ics)
    {
        var calendar = IcalCalendar.Load(ics);
        calendar.ShouldNotBeNull();
        return [.. calendar.Events];
    }

    private static List<string> UnfoldedLines(string ics) =>
        [.. ics.Replace("\r\n ", string.Empty, StringComparison.Ordinal).Split("\r\n")];
}
