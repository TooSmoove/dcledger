using DcLedger.Domain;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

/// <summary>The gate's rules for a decision's terms and status (issue #115): one passing and one failing case per rule.</summary>
public sealed class DecisionTermsRulesTests
{
    private static readonly Sha256Hash OfficialDoc = Sha256Hash.Compute("official"u8);

    private static readonly DecisionTerms FullTerms = new()
    {
        InstrumentNumber = "26-01",
        Threshold = "electrical power demand exceeding 10 megawatts",
        Scope = "development approvals for data center facilities",
        EffectiveOn = new DateOnly(2026, 11, 5),
        ExpiresOn = new DateOnly(2027, 5, 4),
        EarlyTermination = "upon adoption of text amendments",
        Exemptions = "applications deemed complete before the effective date",
        Status = ActionStatus.InEffect,
    };

    [Theory]
    [InlineData(ActionOutcome.Adopted)]
    [InlineData(ActionOutcome.AdoptedAmended)]
    public void Terms_on_an_adopted_action_are_valid(ActionOutcome outcome)
    {
        Validate(Action() with { Outcome = outcome, Terms = FullTerms }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ActionOutcome.Failed)]
    [InlineData(ActionOutcome.Continued)]
    [InlineData(ActionOutcome.Tabled)]
    public void Terms_on_an_action_that_was_not_adopted_are_reported(ActionOutcome outcome)
    {
        var error = Validate(Action() with { Outcome = outcome, Terms = new DecisionTerms { Threshold = "10 megawatts" } }).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.TermsInvalid);
        error.RecordId.ShouldBe("fixture-moratorium-2026-11-05");
    }

    [Fact]
    public void Status_alone_on_an_action_that_was_not_adopted_is_reported()
    {
        Rule(Action() with { Outcome = ActionOutcome.Failed, Terms = new DecisionTerms { Status = ActionStatus.Expired } })
            .ShouldBe(ValidationRule.TermsInvalid);
    }

    [Fact]
    public void An_action_that_was_not_adopted_and_has_no_terms_is_valid()
    {
        Validate(Action() with { Outcome = ActionOutcome.Failed }).ShouldBeEmpty();
    }

    [Fact]
    public void Expiry_on_the_start_date_is_valid()
    {
        var sameDay = new DecisionTerms { EffectiveOn = new DateOnly(2026, 11, 5), ExpiresOn = new DateOnly(2026, 11, 5) };

        Validate(Action() with { Terms = sameDay }).ShouldBeEmpty();
    }

    [Fact]
    public void Expiry_before_the_start_date_is_reported()
    {
        var backwards = new DecisionTerms { EffectiveOn = new DateOnly(2026, 11, 5), ExpiresOn = new DateOnly(2026, 11, 4) };

        var error = Validate(Action() with { Terms = backwards }).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.TermsInvalid);
        error.Message.ShouldBe("expires_on 2026-11-04 is before effective_on 2026-11-05.");
    }

    [Fact]
    public void Expiry_without_a_start_date_is_valid()
    {
        Validate(Action() with { Terms = new DecisionTerms { ExpiresOn = new DateOnly(2027, 2, 14) } }).ShouldBeEmpty();
    }

    [Fact]
    public void A_successor_in_the_same_jurisdiction_decided_later_is_valid()
    {
        var first = Action() with { Terms = new DecisionTerms { SuccessorId = Extension().Id, Status = ActionStatus.Superseded } };

        Validate(first, Extension()).ShouldBeEmpty();
    }

    [Fact]
    public void A_successor_decided_the_same_day_is_valid()
    {
        var first = Action() with { Terms = new DecisionTerms { SuccessorId = Extension().Id } };

        Validate(first, Extension() with { DecidedOn = first.DecidedOn }).ShouldBeEmpty();
    }

    [Fact]
    public void A_successor_that_is_not_in_the_ledger_is_reported()
    {
        var error = Validate(Action() with { Terms = new DecisionTerms { SuccessorId = "fixture-moratorium-2027-05-04" } }).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.FkMissing);
        error.Message.ShouldBe("successor action 'fixture-moratorium-2027-05-04' is missing or failed to load.");
    }

    [Fact]
    public void An_action_cannot_succeed_itself()
    {
        Rule(Action() with { Terms = new DecisionTerms { SuccessorId = Action().Id } }).ShouldBe(ValidationRule.TermsInvalid);
    }

    [Fact]
    public void A_successor_in_another_jurisdiction_is_reported()
    {
        var first = Action() with { Terms = new DecisionTerms { SuccessorId = Extension().Id } };

        Rule(first, Extension() with { JurisdictionId = "other" }).ShouldBe(ValidationRule.TermsInvalid);
    }

    [Fact]
    public void A_successor_decided_earlier_is_reported()
    {
        var first = Action() with { Terms = new DecisionTerms { SuccessorId = Extension().Id } };

        Rule(first, Extension() with { DecidedOn = first.DecidedOn.AddDays(-1) }).ShouldBe(ValidationRule.TermsInvalid);
    }

    [Fact]
    public void Superseded_without_a_successor_is_reported()
    {
        var error = Validate(Action() with { Terms = new DecisionTerms { Status = ActionStatus.Superseded } }).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.TermsInvalid);
        error.Message.ShouldContain("successor_id");
    }

    [Theory]
    [InlineData(ActionStatus.InEffect)]
    [InlineData(ActionStatus.Expired)]
    [InlineData(ActionStatus.Repealed)]
    public void Every_other_status_stands_without_a_successor(ActionStatus status)
    {
        Validate(Action() with { Terms = new DecisionTerms { Status = status } }).ShouldBeEmpty();
    }

    private static IReadOnlyList<ValidationError> Validate(params LocalAction[] actions) => LedgerValidator.Validate(Snapshot(actions), []);

    private static string Rule(params LocalAction[] actions) => Validate(actions).ShouldHaveSingleItem().Rule;

    /// <summary>Two jurisdictions, one official document and the given actions; no events.</summary>
    private static LedgerSnapshot Snapshot(LocalAction[] actions) => new()
    {
        Jurisdictions =
        [
            new("jurisdictions/fixture.yaml", Jurisdiction("fixture")),
            new("jurisdictions/other.yaml", Jurisdiction("other")),
        ],
        Documents = [new($"documents/{OfficialDoc}.yaml", Document())],
        Events = [],
        Actions = [.. actions.Select(action => new LoadedRecord<LocalAction>($"actions/{action.Id}.yaml", action))],
        RecordFiles = ["jurisdictions/fixture.yaml", "jurisdictions/other.yaml", $"documents/{OfficialDoc}.yaml", .. actions.Select(action => $"actions/{action.Id}.yaml")],
    };

    private static Jurisdiction Jurisdiction(string id) => new() { Id = id, Name = "Fixture County", Platform = Platform.Manual };

    private static Document Document() => new()
    {
        Id = OfficialDoc,
        Url = new Uri("https://example.org/ordinance.pdf"),
        RetrievedAtUtc = new DateTimeOffset(2026, 11, 6, 13, 0, 0, TimeSpan.Zero),
        MediaType = "application/pdf",
        Bytes = 1,
        SourceType = SourceType.Official,
    };

    private static LocalAction Action() => new()
    {
        Id = "fixture-moratorium-2026-11-05",
        JurisdictionId = "fixture",
        ActionType = ActionType.Moratorium,
        Outcome = ActionOutcome.Adopted,
        DecidedOn = new DateOnly(2026, 11, 5),
        Votes = "9-0",
        Label = EvidenceLabel.ConfirmedOfficial,
        DocumentIds = [OfficialDoc],
    };

    /// <summary>A later action extending the first one.</summary>
    private static LocalAction Extension() => Action() with { Id = "fixture-moratorium-2027-05-04", DecidedOn = new DateOnly(2027, 5, 4) };
}
