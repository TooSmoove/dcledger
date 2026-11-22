using DcLedger.Domain;
using DcLedger.Testing;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

/// <summary>Known dates from the whole ledger (ADR-0010): what counts as on the record, and the order.</summary>
public sealed class KnownDatesTests
{
    private static readonly DateOnly D = new(2026, 12, 1);

    [Fact]
    public void Every_state_is_worked_out_from_the_ledger_and_D()
    {
        var dates = KnownDates.From(Actions(), Events(), D);

        dates.Select(d => (d.Date.ActionId, d.Date.Kind, d.State)).ShouldBe(
        [
            (ExportSamples.October1Continuance.Id, KnownDateKind.Next, KnownDateState.Recorded),
            (ExportSamples.May4Moratorium.Id, KnownDateKind.Expiry, KnownDateState.Ended),
            (ExportSamples.October20Continuance.Id, KnownDateKind.Next, KnownDateState.PassedUnrecorded),
            (ExportSamples.November5WithDates.Id, KnownDateKind.Next, KnownDateState.Upcoming),
            (ExportSamples.November5WithDates.Id, KnownDateKind.Expiry, KnownDateState.Upcoming),
        ]);
    }

    [Fact]
    public void A_decision_taken_on_the_date_records_a_next_step()
    {
        var decided = ExportSamples.November5Moratorium with { Id = "fixture-city-rezoning-2026-11-17", JurisdictionId = ExportSamples.City.Id, DecidedOn = new DateOnly(2026, 11, 17) };

        var date = KnownDates.From([ExportSamples.October20Continuance, decided], [], D).ShouldHaveSingleItem();

        date.State.ShouldBe(KnownDateState.Recorded);
    }

    [Fact]
    public void A_meeting_in_another_jurisdiction_on_the_date_records_nothing()
    {
        var elsewhere = ExportSamples.November5 with { StartsAt = new DateTimeOffset(2026, 11, 17, 18, 0, 0, TimeSpan.FromHours(-5)) };

        KnownDates.From([ExportSamples.October20Continuance], [elsewhere], D).ShouldHaveSingleItem().State.ShouldBe(KnownDateState.PassedUnrecorded);
    }

    [Fact]
    public void A_meeting_counts_by_its_eastern_date()
    {
        // 11:30 pm Eastern on Nov 17 is Nov 18 in UTC; it is still on the record for Nov 17.
        var late = ExportSamples.November3 with { StartsAt = new DateTimeOffset(2026, 11, 17, 23, 30, 0, TimeSpan.FromHours(-5)) };

        KnownDates.From([ExportSamples.October20Continuance], [late], D).ShouldHaveSingleItem().State.ShouldBe(KnownDateState.Recorded);
    }

    [Fact]
    public void Same_ledger_and_D_give_the_same_list()
    {
        KnownDates.From(Actions(), Events(), D).ShouldBe(KnownDates.From(Actions().Reverse(), Events().Reverse(), D));
    }

    [Fact]
    public void The_export_model_carries_the_known_dates_with_their_actions()
    {
        var model = ExportSamples.ModelWithKnownDates();

        model.Today.ShouldBe(D);
        model.KnownDates.Count.ShouldBe(5);
        model.KnownDates.ShouldAllBe(known => known.Action.Action.Id == known.Date.ActionId);
        model.KnownDates.Single(known => known.Date.Kind == KnownDateKind.Expiry && known.State == KnownDateState.Upcoming)
            .Action.Jurisdiction.ShouldBe(ExportSamples.County);
    }

    private static LocalAction[] Actions() =>
        [ExportSamples.October20Continuance, ExportSamples.November5WithDates, ExportSamples.October1Continuance, ExportSamples.May4Moratorium];

    private static LedgerEvent[] Events() => [ExportSamples.November5, ExportSamples.October15, ExportSamples.November3];
}
