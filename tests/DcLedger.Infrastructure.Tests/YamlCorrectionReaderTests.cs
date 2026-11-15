using DcLedger.Application;
using DcLedger.Infrastructure.Ledger;
using Shouldly;
using Xunit;

namespace DcLedger.Infrastructure.Tests;

/// <summary>Reading corrections log entries from <c>data/corrections/</c> (issue #117).</summary>
public sealed class YamlCorrectionReaderTests
{
    private const string EntryPath = "corrections/2026-11-24-fixture-vote-count.yaml";

    private const string ValidEntry = """
        id: 2026-11-24-fixture-vote-count
        reported_on: 2026-11-23
        corrected_on: 2026-11-24
        record_ids:
          - fixture-moratorium-2026-11-05
        summary: "The vote was recorded as 9-0; the minutes show 8-1."
        issue: 131
        """;

    [Fact]
    public void A_complete_entry_is_read()
    {
        var snapshot = Load(ValidEntry);

        snapshot.LoadErrors.ShouldBeEmpty();
        snapshot.RecordFiles.ShouldHaveSingleItem().ShouldBe(EntryPath);
        var correction = snapshot.Corrections.ShouldHaveSingleItem().Record;
        correction.Id.ShouldBe("2026-11-24-fixture-vote-count");
        correction.ReportedOn.ShouldBe(new DateOnly(2026, 11, 23));
        correction.CorrectedOn.ShouldBe(new DateOnly(2026, 11, 24));
        correction.RecordIds.ShouldBe(["fixture-moratorium-2026-11-05"]);
        correction.Summary.ShouldBe("The vote was recorded as 9-0; the minutes show 8-1.");
        correction.Issue.ShouldBe(131);
    }

    [Fact]
    public void Record_ids_and_issue_are_optional()
    {
        var correction = Load("""
            id: 2026-11-24-fixture-vote-count
            reported_on: 2026-11-23
            corrected_on: 2026-11-24
            summary: "The methodology page gave the wrong publish interval."
            """).Corrections.ShouldHaveSingleItem().Record;

        correction.RecordIds.ShouldBeEmpty();
        correction.Issue.ShouldBeNull();
    }

    [Theory]
    [InlineData("reported_on")]
    [InlineData("corrected_on")]
    [InlineData("summary")]
    public void A_missing_required_key_is_reported(string key)
    {
        var yaml = string.Join('\n', ValidEntry.Split('\n').Where(line => !line.StartsWith(key + ":", StringComparison.Ordinal)));

        var snapshot = Load(yaml);

        snapshot.Corrections.ShouldBeEmpty();
        snapshot.LoadErrors.ShouldHaveSingleItem().Rule.ShouldBe(ValidationRule.RequiredField);
    }

    [Fact]
    public void A_summary_over_the_cap_is_reported()
    {
        var snapshot = Load(ValidEntry.Replace("\"The vote was recorded as 9-0; the minutes show 8-1.\"", new string('x', 501), StringComparison.Ordinal));

        snapshot.Corrections.ShouldBeEmpty();
        snapshot.LoadErrors.ShouldHaveSingleItem().Message.ShouldContain("capped at 500");
    }

    [Fact]
    public void A_summary_with_a_line_break_is_reported()
    {
        var snapshot = Load(ValidEntry.Replace(
            "summary: \"The vote was recorded as 9-0; the minutes show 8-1.\"",
            "summary: |\n  The vote was recorded as 9-0.\n  The minutes show 8-1.",
            StringComparison.Ordinal));

        snapshot.Corrections.ShouldBeEmpty();
        snapshot.LoadErrors.ShouldHaveSingleItem().Message.ShouldContain("single line");
    }

    [Fact]
    public void A_record_id_that_is_not_a_slug_is_reported()
    {
        var snapshot = Load(ValidEntry.Replace("- fixture-moratorium-2026-11-05", "- Fixture Moratorium", StringComparison.Ordinal));

        snapshot.Corrections.ShouldBeEmpty();
        snapshot.LoadErrors.ShouldHaveSingleItem().Rule.ShouldBe(ValidationRule.InvalidValue);
    }

    [Fact]
    public void A_personal_name_key_is_forbidden()
    {
        // Who reported a correction isn't stored (SPEC §E): the linked issue is public on its own terms.
        var snapshot = Load(ValidEntry + "\npetitioner: Someone\n");

        snapshot.LoadErrors.ShouldHaveSingleItem().Rule.ShouldBe(ValidationRule.ForbiddenField);
    }

    [Fact]
    public void An_unknown_key_is_reported()
    {
        Load(ValidEntry + "\nreported_by: someone\n").LoadErrors.ShouldHaveSingleItem().Rule.ShouldBe(ValidationRule.UnknownField);
    }

    [Fact]
    public void Corrections_paths_are_record_paths()
    {
        YamlLedgerReader.IsRecordPath(EntryPath).ShouldBeTrue();
    }

    private static LedgerSnapshot Load(string yaml)
    {
        using var ledger = new TempLedger().Write(EntryPath, yaml);
        return new YamlLedgerReader().Load(ledger.Path);
    }
}
