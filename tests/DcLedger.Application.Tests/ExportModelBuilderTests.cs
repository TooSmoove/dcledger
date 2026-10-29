using DcLedger.Domain;
using DcLedger.Testing;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

public sealed class ExportModelBuilderTests
{
    [Fact]
    public void Events_are_ordered_by_start_time_then_id_and_jurisdictions_by_id()
    {
        var model = ExportSamples.Model();

        model.Events.Select(e => e.Event.Id).ShouldBe(
            [ExportSamples.October15.Id, ExportSamples.November3.Id, ExportSamples.November5.Id]);
        model.Jurisdictions.Select(j => j.Jurisdiction.Id).ShouldBe([ExportSamples.City.Id, ExportSamples.County.Id]);
    }

    [Fact]
    public void Events_with_the_same_start_are_ordered_by_id()
    {
        var twin = ExportSamples.November5 with { Id = "a-fixture-twin", PlatformMeetingId = "1601" };
        var snapshot = ExportSamples.Snapshot() with
        {
            Events = [.. ExportSamples.Snapshot().Events, new LoadedRecord<LedgerEvent>("events/a-fixture-twin.yaml", twin)],
        };

        var model = ExportModelBuilder.Build(snapshot, ExportSamples.Status()).Model;

        model.Events.TakeLast(2).Select(e => e.Event.Id).ShouldBe(["a-fixture-twin", ExportSamples.November5.Id]);
    }

    [Fact]
    public void Each_event_keeps_its_documents_in_record_order_and_its_jurisdiction()
    {
        var october15 = ExportSamples.Model().Events[0];

        october15.Documents.Select(d => d.Id).ShouldBe([ExportSamples.NewsStory.Id, ExportSamples.CountyAgenda1599.Id]);
        october15.Jurisdiction.ShouldBe(ExportSamples.County);
    }

    [Fact]
    public void Automated_jurisdictions_take_last_checked_from_the_status_and_go_stale_against_its_time()
    {
        var model = ExportSamples.Model();

        var county = model.Jurisdictions.Single(j => j.Jurisdiction.Id == ExportSamples.County.Id);
        county.LastCheckedUtc.ShouldBe(new DateTimeOffset(2026, 11, 5, 12, 17, 43, TimeSpan.Zero));
        county.Stale.ShouldBeFalse();
        model.GeneratedUtc.ShouldBe(ExportSamples.GeneratedUtc);
    }

    [Fact]
    public void Manual_jurisdictions_take_last_checked_from_their_record_and_ignore_the_status()
    {
        var status = ExportSamples.Status() with
        {
            LastCheckedUtc = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal) { [ExportSamples.City.Id] = ExportSamples.GeneratedUtc },
        };

        var result = ExportModelBuilder.Build(ExportSamples.Snapshot(), status);

        var city = result.Model.Jurisdictions.Single(j => j.Jurisdiction.Id == ExportSamples.City.Id);
        city.LastCheckedUtc.ShouldBe(ExportSamples.City.LastManualCheckUtc);
        city.Stale.ShouldBeTrue();
        result.Warnings.ShouldHaveSingleItem().ShouldContain("'fixture-city'");
    }

    [Fact]
    public void An_automated_jurisdiction_missing_from_the_status_was_never_checked()
    {
        var status = ExportSamples.Status() with { LastCheckedUtc = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal) };

        var result = ExportModelBuilder.Build(ExportSamples.Snapshot(), status);

        var county = result.Model.Jurisdictions.Single(j => j.Jurisdiction.Id == ExportSamples.County.Id);
        county.LastCheckedUtc.ShouldBeNull();
        county.Stale.ShouldBeTrue();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void The_stale_flag_follows_the_48_hour_rule_against_the_status_time()
    {
        var checkedAt = ExportSamples.GeneratedUtc.AddHours(-49);
        var status = ExportSamples.Status() with
        {
            LastCheckedUtc = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal) { [ExportSamples.County.Id] = checkedAt },
        };

        var county = ExportModelBuilder.Build(ExportSamples.Snapshot(), status).Model.Jurisdictions
            .Single(j => j.Jurisdiction.Id == ExportSamples.County.Id);

        county.Stale.ShouldBeTrue();
    }

    [Fact]
    public void Status_entries_for_unknown_jurisdictions_are_ignored_with_a_warning()
    {
        var status = ExportSamples.Status() with
        {
            LastCheckedUtc = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal)
            {
                [ExportSamples.County.Id] = ExportSamples.GeneratedUtc,
                ["retired-county"] = ExportSamples.GeneratedUtc,
            },
        };

        var result = ExportModelBuilder.Build(ExportSamples.Snapshot(), status);

        result.Warnings.ShouldHaveSingleItem()
            .ShouldBe("The ingest status lists 'retired-county', which isn't an automated jurisdiction in the ledger; ignored.");
        result.Model.Jurisdictions.Count.ShouldBe(2);
    }

    [Fact]
    public void An_unvalidated_snapshot_with_a_missing_document_is_a_bug()
    {
        var snapshot = ExportSamples.Snapshot() with { Documents = [] };

        Should.Throw<InvalidOperationException>(() => ExportModelBuilder.Build(snapshot, ExportSamples.Status()))
            .Message.ShouldContain("Validate the ledger before exporting");
    }
}
