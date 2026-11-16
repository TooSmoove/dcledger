using DcLedger.Domain;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

/// <summary>The gate's rules for a corrections log entry (issue #117): one passing and one failing case per rule.</summary>
public sealed class CorrectionRulesTests
{
    private static readonly Sha256Hash OfficialDoc = Sha256Hash.Compute("official"u8);

    [Fact]
    public void A_complete_entry_naming_a_jurisdiction_and_an_action_is_valid()
    {
        Validate(Entry() with { RecordIds = ["fixture", "fixture-moratorium-2026-11-05"], Report = 131 }).ShouldBeEmpty();
    }

    [Fact]
    public void An_entry_that_names_no_record_is_valid()
    {
        // A correction to page wording changes no record.
        Validate(Entry() with { RecordIds = [] }).ShouldBeEmpty();
    }

    [Fact]
    public void An_entry_reported_and_corrected_on_the_day_the_log_opened_is_valid()
    {
        Validate(Entry() with { ReportedOn = Correction.LogOpenedOn, CorrectedOn = Correction.LogOpenedOn }).ShouldBeEmpty();
    }

    [Fact]
    public void An_entry_reported_before_the_log_opened_is_reported()
    {
        var error = Validate(Entry() with { ReportedOn = Correction.LogOpenedOn.AddDays(-1) }).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.CorrectionInvalid);
        error.RecordId.ShouldBe("2026-11-24-fixture-vote-count");
        error.Message.ShouldContain("before the log opened on 2026-11-22");
    }

    [Fact]
    public void An_entry_corrected_before_it_was_reported_is_reported()
    {
        var error = Validate(Entry() with { ReportedOn = new DateOnly(2026, 11, 25), CorrectedOn = new DateOnly(2026, 11, 24) }).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.CorrectionInvalid);
        error.Message.ShouldContain("before reported_on");
    }

    [Fact]
    public void Report_number_zero_is_reported()
    {
        Rule(Entry() with { Report = 0 }).ShouldBe(ValidationRule.CorrectionInvalid);
    }

    [Fact]
    public void A_record_named_twice_is_reported_once()
    {
        var error = Validate(Entry() with { RecordIds = ["fixture", "fixture"] }).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.CorrectionInvalid);
        error.Message.ShouldContain("more than once");
    }

    [Fact]
    public void A_record_that_is_not_in_the_ledger_is_reported()
    {
        var error = Validate(Entry() with { RecordIds = ["fixture-moratorium-2099-01-01"] }).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.FkMissing);
        error.Message.ShouldContain("'fixture-moratorium-2099-01-01'");
    }

    [Fact]
    public void A_document_hash_is_not_a_correctable_record()
    {
        // Documents are evidence, not published records; a correction names the item the document backs.
        Rule(Entry() with { RecordIds = [OfficialDoc.Value] }).ShouldBe(ValidationRule.FkMissing);
    }

    [Fact]
    public void An_id_that_differs_from_the_file_name_is_reported()
    {
        var snapshot = Snapshot(Entry()) with
        {
            Corrections = [new LoadedRecord<Correction>("corrections/another-name.yaml", Entry())],
        };

        LedgerValidator.Validate(snapshot, []).ShouldHaveSingleItem().Rule.ShouldBe(ValidationRule.IdMismatch);
    }

    [Fact]
    public void A_published_entry_that_is_removed_is_reported()
    {
        // The log is append-only: an entry's file must still exist once it is on main.
        var error = LedgerValidator.Validate(Snapshot(), ["corrections/2026-11-24-fixture-vote-count.yaml"]).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.SlugChanged);
    }

    private static IReadOnlyList<ValidationError> Validate(Correction correction) => LedgerValidator.Validate(Snapshot(correction), []);

    private static string Rule(Correction correction) => Validate(correction).ShouldHaveSingleItem().Rule;

    /// <summary>One jurisdiction, one official document, one action and the given entries.</summary>
    private static LedgerSnapshot Snapshot(params Correction[] corrections) => new()
    {
        Jurisdictions = [new("jurisdictions/fixture.yaml", new Jurisdiction { Id = "fixture", Name = "Fixture County", Platform = Platform.Manual })],
        Documents = [new($"documents/{OfficialDoc}.yaml", Document())],
        Actions = [new("actions/fixture-moratorium-2026-11-05.yaml", Action())],
        Corrections = [.. corrections.Select(correction => new LoadedRecord<Correction>($"corrections/{correction.Id}.yaml", correction))],
        RecordFiles =
        [
            "jurisdictions/fixture.yaml",
            $"documents/{OfficialDoc}.yaml",
            "actions/fixture-moratorium-2026-11-05.yaml",
            .. corrections.Select(correction => $"corrections/{correction.Id}.yaml"),
        ],
    };

    private static Correction Entry() => new()
    {
        Id = "2026-11-24-fixture-vote-count",
        ReportedOn = new DateOnly(2026, 11, 23),
        CorrectedOn = new DateOnly(2026, 11, 24),
        RecordIds = ["fixture-moratorium-2026-11-05"],
        Summary = "The vote was recorded as 9-0; the minutes show 8-1.",
    };

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
        Votes = "8-1",
        Label = EvidenceLabel.ConfirmedOfficial,
        DocumentIds = [OfficialDoc],
    };
}
