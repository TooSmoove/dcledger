using DcLedger.Domain;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

/// <summary>The gate's rule for an action's <c>next_on</c> (ADR-0010 Decision 2): after the decision, on any outcome.</summary>
public sealed class NextOnRulesTests
{
    private static readonly Sha256Hash OfficialDoc = Sha256Hash.Compute("official"u8);

    [Theory]
    [InlineData(ActionOutcome.Adopted)]
    [InlineData(ActionOutcome.Continued)]
    [InlineData(ActionOutcome.Tabled)]
    [InlineData(ActionOutcome.Failed)]
    public void A_next_step_after_the_decision_is_valid_on_any_outcome(ActionOutcome outcome)
    {
        Validate(Action() with { Outcome = outcome, NextOn = new DateOnly(2026, 11, 6) }).ShouldBeEmpty();
    }

    [Fact]
    public void An_action_without_a_next_step_is_valid()
    {
        Validate(Action()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(2026, 11, 5, "the decision's own day")]
    [InlineData(2026, 11, 4, "before the decision")]
    public void A_next_step_on_or_before_the_decision_is_rejected(int year, int month, int day, string because)
    {
        var error = Validate(Action() with { NextOn = new DateOnly(year, month, day) }).ShouldHaveSingleItem(because);

        error.Rule.ShouldBe(ValidationRule.InvalidValue);
        error.RecordId.ShouldBe("fixture-continuance-2026-11-05");
        error.Message.ShouldContain("next_on");
        error.Message.ShouldContain("decided_on 2026-11-05");
    }

    private static IReadOnlyList<ValidationError> Validate(LocalAction action) => LedgerValidator.Validate(Snapshot(action), []);

    private static LedgerSnapshot Snapshot(LocalAction action) => new()
    {
        Jurisdictions = [new("jurisdictions/fixture.yaml", new Jurisdiction { Id = "fixture", Name = "Fixture County", Platform = Platform.Manual })],
        Documents = [new($"documents/{OfficialDoc}.yaml", Document())],
        Events = [],
        Actions = [new($"actions/{action.Id}.yaml", action)],
        RecordFiles = ["jurisdictions/fixture.yaml", $"documents/{OfficialDoc}.yaml", $"actions/{action.Id}.yaml"],
    };

    private static Document Document() => new()
    {
        Id = OfficialDoc,
        Url = new Uri("https://example.org/minutes.pdf"),
        RetrievedAtUtc = new DateTimeOffset(2026, 11, 6, 13, 0, 0, TimeSpan.Zero),
        MediaType = "application/pdf",
        Bytes = 1,
        SourceType = SourceType.Official,
    };

    private static LocalAction Action() => new()
    {
        Id = "fixture-continuance-2026-11-05",
        JurisdictionId = "fixture",
        ActionType = ActionType.Moratorium,
        Outcome = ActionOutcome.Continued,
        DecidedOn = new DateOnly(2026, 11, 5),
        Label = EvidenceLabel.ConfirmedOfficial,
        DocumentIds = [OfficialDoc],
    };
}
