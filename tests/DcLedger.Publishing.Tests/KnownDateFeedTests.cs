using System.Globalization;
using System.Xml.Linq;
using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Publishing.Feed;
using DcLedger.Testing;
using Shouldly;
using Xunit;
using static VerifyXunit.Verifier;

namespace DcLedger.Publishing.Tests;

/// <summary>
/// The feed's known-date items (ADR-0010 Decision 5) for <see cref="ExportSamples.ModelWithKnownDates"/> at D = Dec 1,
/// 2026: reminders 30 and 7 days ahead, and one item the day after a date that passed with nothing recorded.
/// </summary>
public sealed class KnownDateFeedTests
{
    private static readonly Uri Site = new("https://ncdcledger.org");

    private static readonly string NextDec3 = $"known:next:{ExportSamples.November5WithDates.Id}/2026-12-03";
    private static readonly string NextNov17 = $"known:next:{ExportSamples.October20Continuance.Id}/2026-11-17";
    private static readonly string ExpiryJan8 = $"known:expiry:{ExportSamples.November5WithDates.Id}/2027-01-08";

    [Fact]
    public Task Feed_with_known_dates_golden() => Verify(Feed(ExportSamples.ModelWithKnownDates()));

    [Fact]
    public void Due_reminders_and_the_passed_item_are_in_the_feed_and_nothing_else()
    {
        KnownGuids(ExportSamples.ModelWithKnownDates()).ShouldBe(
        [
            $"{NextDec3}/7",      // Nov 26: 7 days before Dec 3; its 30-day day (Nov 3) came before the decision was published
            $"{NextNov17}/passed", // Nov 18: the day after Nov 17, with nothing on the city's record that day
            $"{NextNov17}/7",      // Nov 10
        ]);
    }

    [Fact]
    public void Reminders_and_passed_items_are_dated_00_00_eastern()
    {
        var items = KnownItems(ExportSamples.ModelWithKnownDates()).ToDictionary(item => item.Element("guid")!.Value, StringComparer.Ordinal);

        PubDate(items[$"{NextDec3}/7"]).ShouldBe(new DateTimeOffset(2026, 11, 26, 5, 0, 0, TimeSpan.Zero));
        PubDate(items[$"{NextNov17}/passed"]).ShouldBe(new DateTimeOffset(2026, 11, 18, 5, 0, 0, TimeSpan.Zero));
        PubDate(items[$"{NextNov17}/7"]).ShouldBe(new DateTimeOffset(2026, 11, 10, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_reminder_appears_on_its_day_and_not_before()
    {
        KnownGuids(ExportSamples.ModelWithKnownDates(Eastern(2026, 12, 8, 23))).ShouldNotContain($"{ExpiryJan8}/30");
        KnownGuids(ExportSamples.ModelWithKnownDates(Eastern(2026, 12, 9, 1))).ShouldContain($"{ExpiryJan8}/30");
    }

    [Fact]
    public void Items_already_due_stay_in_the_feed_as_D_moves_on()
    {
        var later = KnownGuids(ExportSamples.ModelWithKnownDates(Eastern(2027, 1, 2, 9)));

        later.ShouldContain($"{NextDec3}/7");
        later.ShouldContain($"{ExpiryJan8}/30");
        later.ShouldContain($"{ExpiryJan8}/7");
        later.ShouldContain($"{NextNov17}/passed");
    }

    [Fact]
    public void An_ended_date_has_no_items()
    {
        KnownGuids(ExportSamples.ModelWithKnownDates(Eastern(2027, 1, 2, 9)))
            .ShouldAllBe(guid => !guid.Contains(ExportSamples.May4Moratorium.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void Recording_the_outcome_removes_the_passed_item_but_keeps_reminders_already_due()
    {
        var model = ExportSamples.ModelWithKnownDates();
        var recorded = model with
        {
            KnownDates = [.. model.KnownDates.Select(k => k.State == KnownDateState.PassedUnrecorded ? k with { State = KnownDateState.Recorded } : k)],
        };

        var guids = KnownGuids(recorded);
        guids.ShouldNotContain($"{NextNov17}/passed");
        guids.ShouldContain($"{NextNov17}/7");
    }

    [Fact]
    public void A_rescheduled_date_gets_fresh_guids_and_the_old_ones_leave()
    {
        var model = ExportSamples.ModelWithKnownDates();
        var moved = model with
        {
            KnownDates =
            [
                .. model.KnownDates.Select(k => k.Date.ActionId == ExportSamples.November5WithDates.Id && k.Date.Kind == KnownDateKind.Next
                    ? k with { Date = k.Date with { Date = new DateOnly(2026, 12, 8) } }
                    : k),
            ],
        };

        var guids = KnownGuids(moved);
        guids.ShouldContain($"known:next:{ExportSamples.November5WithDates.Id}/2026-12-08/7");
        guids.ShouldNotContain($"{NextDec3}/7");
    }

    [Fact]
    public void Item_text_names_the_date_links_its_ahead_row_and_carries_the_decision()
    {
        var items = KnownItems(ExportSamples.ModelWithKnownDates()).ToDictionary(item => item.Element("guid")!.Value, StringComparer.Ordinal);

        var reminder = items[$"{NextDec3}/7"];
        reminder.Element("title")!.Value.ShouldBe("Date set for the next step: Fixture County, moratorium, Thursday, December 3, 2026 (in 7 days)");
        reminder.Element("link")!.Value.ShouldBe($"https://ncdcledger.org/ahead/#{ExportSamples.November5WithDates.Id}-next");
        var html = reminder.Element("description")!.Value;
        html.ShouldContain("Date set for the next step: Thursday, December 3, 2026.");
        html.ShouldContain($"<a href=\"https://ncdcledger.org/items/{ExportSamples.November5.Id}/\">The decision</a>");
        html.ShouldContain(ExportSamples.CountyActionDetail1600.Id.Value);
        html.ShouldContain(RecordText.KnownDateCoverage);

        var passed = items[$"{NextNov17}/passed"];
        passed.Element("title")!.Value.ShouldBe("Date passed, nothing recorded yet: Date set for the next step: Fixture City, rezoning");
        passed.Element("description")!.Value.ShouldContain(RecordText.PassedUnrecorded);
    }

    [Fact]
    public void No_item_text_calls_a_date_a_deadline()
    {
        var feed = Feed(ExportSamples.ModelWithKnownDates(Eastern(2027, 1, 2, 9)));

        feed.ShouldNotContain("deadline", Case.Insensitive);
    }

    [Fact]
    public void The_same_ledger_and_D_give_the_same_bytes()
    {
        Feed(ExportSamples.ModelWithKnownDates()).ShouldBe(Feed(ExportSamples.ModelWithKnownDates()));
    }

    [Fact]
    public void A_later_export_on_the_same_eastern_day_gives_the_same_bytes()
    {
        // pubDates never depend on when publish ran, only on D.
        Feed(ExportSamples.ModelWithKnownDates(Eastern(2026, 12, 1, 6))).ShouldBe(Feed(ExportSamples.ModelWithKnownDates(Eastern(2026, 12, 1, 23))));
    }

    private static DateTimeOffset Eastern(int year, int month, int day, int hour) =>
        new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    private static string Feed(ExportModel model) => TestFiles.Text(TestFiles.Single(new FeedExporter(Site).Export(model), ExportPaths.Feed));

    private static List<XElement> KnownItems(ExportModel model) =>
        [.. XDocument.Parse(Feed(model)).Root!.Element("channel")!.Elements("item").Where(item => item.Element("guid")!.Value.StartsWith("known:", StringComparison.Ordinal))];

    private static List<string> KnownGuids(ExportModel model) => [.. KnownItems(model).Select(item => item.Element("guid")!.Value)];

    private static DateTimeOffset PubDate(XElement item) => DateTimeOffset.ParseExact(
        item.Element("pubDate")!.Value, "ddd, dd MMM yyyy HH:mm:ss '+0000'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
