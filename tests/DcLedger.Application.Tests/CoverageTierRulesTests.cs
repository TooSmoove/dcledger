using DcLedger.Domain;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

/// <summary>
/// What each coverage tier may hold (issue #115): events only in a watched jurisdiction, decisions in a watched or a
/// decisions one, and nothing in a registered one.
/// </summary>
public sealed class CoverageTierRulesTests
{
    private static readonly Sha256Hash OfficialDoc = Sha256Hash.Compute("official"u8);

    [Fact]
    public void A_watched_jurisdiction_holds_events_and_decisions()
    {
        Validate(CoverageTier.Watched, Event(), Action()).ShouldBeEmpty();
    }

    [Fact]
    public void A_decisions_jurisdiction_holds_decisions()
    {
        Validate(CoverageTier.Decisions, null, Action()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(CoverageTier.Decisions)]
    [InlineData(CoverageTier.Registered)]
    public void An_event_outside_a_watched_jurisdiction_is_reported(CoverageTier tier)
    {
        var error = Validate(tier, Event(), null).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.TierMismatch);
        error.File.ShouldBe("events/fixture-boc-2026-11-05.yaml");
        error.Message.ShouldBe($"jurisdiction 'fixture' has coverage_tier {tier.ToToken()}; only a watched jurisdiction has events.");
    }

    [Fact]
    public void A_decision_in_a_registered_jurisdiction_is_reported()
    {
        var error = Validate(CoverageTier.Registered, null, Action()).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.TierMismatch);
        error.File.ShouldBe("actions/fixture-moratorium-2026-11-05.yaml");
    }

    [Theory]
    [InlineData(CoverageTier.Decisions)]
    [InlineData(CoverageTier.Registered)]
    public void A_jurisdiction_with_no_records_is_valid_on_any_tier(CoverageTier tier)
    {
        Validate(tier, null, null).ShouldBeEmpty();
    }

    private static IReadOnlyList<ValidationError> Validate(CoverageTier tier, LedgerEvent? ledgerEvent, LocalAction? action)
    {
        var jurisdiction = new Jurisdiction { Id = "fixture", Name = "Fixture County", Platform = Platform.Manual, CoverageTier = tier };
        var snapshot = new LedgerSnapshot
        {
            Jurisdictions = [new("jurisdictions/fixture.yaml", jurisdiction)],
            Documents = [new($"documents/{OfficialDoc}.yaml", Document())],
            Events = ledgerEvent is null ? [] : [new($"events/{ledgerEvent.Id}.yaml", ledgerEvent)],
            Actions = action is null ? [] : [new($"actions/{action.Id}.yaml", action)],
        };
        return LedgerValidator.Validate(snapshot, []);
    }

    private static Document Document() => new()
    {
        Id = OfficialDoc,
        Url = new Uri("https://example.org/minutes.pdf"),
        RetrievedAtUtc = new DateTimeOffset(2026, 11, 6, 13, 0, 0, TimeSpan.Zero),
        MediaType = "application/pdf",
        Bytes = 1,
        SourceType = SourceType.Official,
    };

    private static LedgerEvent Event() => new()
    {
        Id = "fixture-boc-2026-11-05",
        JurisdictionId = "fixture",
        Platform = Platform.Manual,
        PlatformMeetingId = "2026-11-05 regular",
        Body = "Board of Commissioners",
        Title = "Fixture: regular meeting",
        StartsAt = new DateTimeOffset(2026, 11, 5, 17, 30, 0, TimeSpan.FromHours(-5)),
        Status = EventStatus.Scheduled,
        Sequence = 0,
        Label = EvidenceLabel.ConfirmedOfficial,
        DocumentIds = [OfficialDoc],
    };

    /// <summary>A decision with no meeting in the ledger, so it stands without an event.</summary>
    private static LocalAction Action() => new()
    {
        Id = "fixture-moratorium-2026-11-05",
        JurisdictionId = "fixture",
        ActionType = ActionType.Moratorium,
        Outcome = ActionOutcome.Adopted,
        DecidedOn = new DateOnly(2026, 11, 5),
        Label = EvidenceLabel.ConfirmedOfficial,
        DocumentIds = [OfficialDoc],
    };
}
