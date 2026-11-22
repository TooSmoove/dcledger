using System.Text;
using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Publishing.Ics;
using DcLedger.Testing;
using Ical.Net.CalendarComponents;
using Shouldly;
using Xunit;
using static VerifyXunit.Verifier;
using IcalCalendar = Ical.Net.Calendar;

namespace DcLedger.Publishing.Tests;

/// <summary>The known-date calendars (ADR-0010 Decision 4): all-day, stable UIDs, SEQUENCE, CANCELLED, no alarms.</summary>
public sealed class AheadCalendarExporterTests
{
    private static readonly Uri Site = new("https://ncdcledger.org");

    // Verify compares text with LF line endings; CRLF is asserted byte for byte below.
    [Fact]
    public Task Ahead_golden() => Verify(Ics(ExportPaths.Ahead).ReplaceLineEndings("\n"));

    [Fact]
    public Task Jurisdiction_ahead_golden() => Verify(Ics(ExportPaths.JurisdictionAhead(ExportSamples.City.Id)).ReplaceLineEndings("\n"));

    [Fact]
    public void There_is_one_calendar_for_all_and_one_per_jurisdiction()
    {
        Files().Select(file => file.Path).ShouldBe(
        [
            ExportPaths.Ahead,
            ExportPaths.JurisdictionAhead(ExportSamples.City.Id),
            ExportPaths.JurisdictionAhead(ExportSamples.County.Id),
        ]);
        Events(Ics(ExportPaths.Ahead)).Count.ShouldBe(5, "every known date, whatever its state");
        Events(Ics(ExportPaths.JurisdictionAhead(ExportSamples.City.Id))).Select(e => e.Uid)
            .ShouldBe([KnownDateRules.Uid(KnownDateKind.Next, ExportSamples.October20Continuance.Id)]);
        Events(Ics(ExportPaths.JurisdictionAhead(ExportSamples.County.Id))).Count.ShouldBe(4);
    }

    [Fact]
    public void A_decisions_only_jurisdiction_gets_a_calendar_and_a_registered_one_does_not()
    {
        var model = WithTier(ExportSamples.City.Id, CoverageTier.Decisions);
        new AheadCalendarExporter(Site).Export(model).Select(f => f.Path).ShouldContain(ExportPaths.JurisdictionAhead(ExportSamples.City.Id));

        var registered = WithTier(ExportSamples.City.Id, CoverageTier.Registered);
        new AheadCalendarExporter(Site).Export(registered).Select(f => f.Path).ShouldNotContain(ExportPaths.JurisdictionAhead(ExportSamples.City.Id));
    }

    [Fact]
    public void A_jurisdiction_without_known_dates_still_gets_a_valid_calendar()
    {
        var files = new AheadCalendarExporter(Site).Export(ExportSamples.Model());

        files.Select(f => f.Path).ShouldBe([ExportPaths.Ahead, ExportPaths.JurisdictionAhead(ExportSamples.City.Id), ExportPaths.JurisdictionAhead(ExportSamples.County.Id)]);
        files.ShouldAllBe(file => TestFiles.Text(file).Contains("END:VCALENDAR", StringComparison.Ordinal));
        files.ShouldAllBe(file => TestFiles.Text(file).Contains(PinnedTimeZone.Block, StringComparison.Ordinal), "a calendar needs one component");
    }

    [Fact]
    public void Each_date_is_an_all_day_event_with_no_timezone_and_no_alarm()
    {
        var text = Ics(ExportPaths.Ahead);
        var lines = UnfoldedLines(text);

        lines.ShouldContain("DTSTART;VALUE=DATE:20270108");
        lines.ShouldContain("DTEND;VALUE=DATE:20270109");
        lines.ShouldContain("DTSTART;VALUE=DATE:20261203");
        lines.ShouldContain("DTEND;VALUE=DATE:20261204");
        text.ShouldNotContain("VTIMEZONE");
        text.ShouldNotContain("TZID");
        text.ShouldNotContain("VALARM");
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
    public void An_ended_expiry_is_cancelled_with_its_raised_sequence_and_others_are_not()
    {
        var events = Events(Ics(ExportPaths.Ahead)).ToDictionary(e => e.Uid!, StringComparer.Ordinal);

        var ended = events[KnownDateRules.Uid(KnownDateKind.Expiry, ExportSamples.May4Moratorium.Id)];
        ended.Status.ShouldBe("CANCELLED");
        ended.Sequence.ShouldBe(2);

        events.Values.Where(e => e != ended).ShouldAllBe(e => e.Status == null);
    }

    [Fact]
    public void Summaries_use_the_fixed_names()
    {
        var summaries = Events(Ics(ExportPaths.Ahead)).Select(e => e.Summary).ToList();

        summaries.ShouldContain("Moratorium expiry date: Fixture County");
        summaries.ShouldContain("Date set for the next step: Fixture City, rezoning");
        summaries.ShouldContain("Date set for the next step: Fixture County, ordinance text amendment");
        summaries.ShouldAllBe(s => !s!.Contains("deadline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_rescheduled_date_keeps_its_uid_and_takes_the_raised_sequence()
    {
        var model = ExportSamples.ModelWithKnownDates();
        var moved = model with
        {
            KnownDates =
            [
                .. model.KnownDates.Select(k => k.Date.Kind == KnownDateKind.Next && k.Date.ActionId == ExportSamples.November5WithDates.Id
                    ? k with { Date = k.Date with { Date = new DateOnly(2026, 12, 17) }, Action = k.Action with { Action = k.Action.Action with { Sequence = 1 } } }
                    : k),
            ],
        };

        var uid = KnownDateRules.Uid(KnownDateKind.Next, ExportSamples.November5WithDates.Id);
        var vevent = Events(TestFiles.Text(TestFiles.Single(new AheadCalendarExporter(Site).Export(moved), ExportPaths.Ahead))).Single(e => e.Uid == uid);
        vevent.Sequence.ShouldBe(1);
        vevent.DtStart!.Date.ShouldBe(new DateOnly(2026, 12, 17));
    }

    [Fact]
    public void The_description_holds_terms_label_sources_and_links_but_no_prediction()
    {
        var events = Events(Ics(ExportPaths.Ahead)).ToDictionary(e => e.Uid!, StringComparer.Ordinal);
        var expiry = events[KnownDateRules.Uid(KnownDateKind.Expiry, ExportSamples.November5WithDates.Id)].Description!;

        expiry.ShouldContain("Expiry date: Friday, January 8, 2027.");
        expiry.ShouldContain("Threshold: “electrical power demand exceeding 10 megawatts”");
        expiry.ShouldContain("Status: In effect");
        expiry.ShouldContain("Confirmed (official record)");
        expiry.ShouldContain($"Source: {ExportSamples.CountyActionDetail1600.Url.AbsoluteUri}");
        expiry.ShouldContain($"SHA-256 {ExportSamples.CountyActionDetail1600.Id.Value}");
        expiry.ShouldContain($"Decision: https://ncdcledger.org/items/{ExportSamples.November5.Id}/");
        expiry.ShouldContain($"Coming up on the record: https://ncdcledger.org/ahead/#{ExportSamples.November5WithDates.Id}-expiry");
        expiry.ShouldContain(RecordText.KnownDateCoverage);

        var passed = events[KnownDateRules.Uid(KnownDateKind.Next, ExportSamples.October20Continuance.Id)].Description!;
        passed.ShouldContain(RecordText.PassedUnrecorded);
        passed.ShouldContain($"Decision: https://ncdcledger.org/jurisdictions/{ExportSamples.City.Id}/");
        passed.ShouldContain(RetrievalNote.Manual);
    }

    private static ExportModel WithTier(string jurisdictionId, CoverageTier tier)
    {
        var model = ExportSamples.ModelWithKnownDates();
        return model with
        {
            Jurisdictions = [.. model.Jurisdictions.Select(j => j.Jurisdiction.Id == jurisdictionId ? j with { Jurisdiction = j.Jurisdiction with { CoverageTier = tier } } : j)],
        };
    }

    private static IReadOnlyList<ExportFile> Files() => new AheadCalendarExporter(Site).Export(ExportSamples.ModelWithKnownDates());

    private static string Ics(string path) => TestFiles.Text(TestFiles.Single(Files(), path));

    private static List<CalendarEvent> Events(string ics) => [.. IcalCalendar.Load(ics)!.Events];

    private static List<string> UnfoldedLines(string ics) =>
        [.. ics.Replace("\r\n ", string.Empty, StringComparison.Ordinal).Split("\r\n")];
}
