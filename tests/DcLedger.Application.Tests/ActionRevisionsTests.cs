using DcLedger.Domain;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

public sealed class ActionRevisionsTests
{
    private const string ActionFile = "actions/fixture-moratorium-2026-11-05.yaml";
    private static readonly Sha256Hash News = Sha256Hash.Compute("news"u8);
    private static readonly Sha256Hash Minutes = Sha256Hash.Compute("minutes"u8);

    [Fact]
    public void Unchanged_action_is_fine()
    {
        Check(Reported(), Reported()).ShouldBeEmpty();
    }

    [Fact]
    public void Confirming_a_decision_with_a_raised_sequence_is_fine()
    {
        Check(Reported(), Confirmed() with { Sequence = 1 }).ShouldBeEmpty();
    }

    [Fact]
    public void Confirming_a_decision_without_a_raised_sequence_is_rejected()
    {
        var problem = Check(Reported(), Confirmed()).ShouldHaveSingleItem();

        problem.Rule.ShouldBe(ValidationRule.SequenceNotRaised);
        problem.File.ShouldBe(ActionFile);
        problem.RecordId.ShouldBe("fixture-moratorium-2026-11-05");
        problem.Message.ShouldBe("votes, label, document_ids changed but sequence is still 0. Raise it to 1 so feed readers see the change.");
    }

    [Fact]
    public void Every_published_field_is_named_in_yaml_terms()
    {
        var changed = Reported() with
        {
            EventId = "fixture-boc-2026-11-06",
            ActionType = ActionType.Resolution,
            Outcome = ActionOutcome.Continued,
            DecidedOn = new DateOnly(2026, 11, 6),
            Votes = "8-1",
            Unanimous = false,
            Label = EvidenceLabel.ConfirmedOfficial,
            DocumentIds = [Minutes],
        };

        ActionRevisions.ChangedFields(Reported(), changed).ShouldBe(
            new List<string> { "event_id", "action_type", "outcome", "decided_on", "votes", "unanimous", "label", "document_ids" });
    }

    [Fact]
    public void Sequence_never_goes_down()
    {
        Check(Reported() with { Sequence = 2 }, Reported() with { Sequence = 1 }).ShouldHaveSingleItem()
            .Message.ShouldBe("sequence went down from 2 to 1; it only goes up.");
    }

    [Fact]
    public void Jurisdiction_is_frozen_even_with_a_raised_sequence()
    {
        Check(Reported(), Reported() with { JurisdictionId = "other", Sequence = 1 }).ShouldHaveSingleItem()
            .Rule.ShouldBe(ValidationRule.JurisdictionChanged);
    }

    [Fact]
    public void New_and_removed_actions_are_left_to_other_rules()
    {
        var other = Reported() with { Id = "fixture-resolution-2026-11-19" };

        ActionRevisions.Check([new(ActionFile, Reported())], [new("actions/fixture-resolution-2026-11-19.yaml", other)]).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_ledger_checks_actions_when_given_the_previous_data()
    {
        var previous = new LedgerSnapshot { Actions = [new(ActionFile, Reported())] };
        var current = new LedgerSnapshot { Actions = [new(ActionFile, Confirmed())] };

        ValidateLedger.Check(current, [], previous).Errors.ShouldContain(error => error.Rule == ValidationRule.SequenceNotRaised);
        ValidateLedger.Check(current, []).Errors.ShouldNotContain(error => error.Rule == ValidationRule.SequenceNotRaised);
    }

    private static IReadOnlyList<ValidationError> Check(LocalAction was, LocalAction now) =>
        ActionRevisions.Check([new(ActionFile, now)], [new(ActionFile, was)]);

    /// <summary>The Nov 5 decision as first recorded from news coverage: unanimous, no tally.</summary>
    private static LocalAction Reported() => new()
    {
        Id = "fixture-moratorium-2026-11-05",
        JurisdictionId = "fixture",
        EventId = "fixture-boc-2026-11-05",
        ActionType = ActionType.Moratorium,
        Outcome = ActionOutcome.Adopted,
        DecidedOn = new DateOnly(2026, 11, 5),
        Unanimous = true,
        Label = EvidenceLabel.ReportedNews,
        DocumentIds = [News],
    };

    /// <summary>The same decision once the minutes are in: tally, official record, minutes added.</summary>
    private static LocalAction Confirmed() => Reported() with
    {
        Votes = "9-0",
        Label = EvidenceLabel.ConfirmedOfficial,
        DocumentIds = [News, Minutes],
    };
}
