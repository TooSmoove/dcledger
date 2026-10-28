using System.Text.RegularExpressions;
using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Infrastructure.Ledger;
using Shouldly;
using Xunit;

namespace DcLedger.Infrastructure.Tests.GitHub;

/// <summary>The YAML a review issue offers passes <c>dcl validate</c> once the reviewer writes a title, and not before.</summary>
public sealed partial class RenderedIssueYamlTests
{
    private static readonly string[] DataCenterRule = ["data_center"];

    [Fact]
    public void Offered_records_validate_once_the_title_is_written()
    {
        using var ledger = Ledger(Candidate(), title: "Public hearing on a data center rezoning");

        Validate(ledger).ShouldBeEmpty();
    }

    [Fact]
    public void Untouched_title_fails_validation()
    {
        using var ledger = Ledger(Candidate(), title: null);

        Validate(ledger).ShouldHaveSingleItem().ShouldBe(("title", ValidationRule.RequiredField));
    }

    [Fact]
    public void Missing_start_time_fails_validation_until_a_reviewer_supplies_it()
    {
        using var ledger = Ledger(Candidate() with { Time = null, StartsAt = null }, title: "Neutral title");

        Validate(ledger).ShouldHaveSingleItem().ShouldBe(("starts_at", ValidationRule.RequiredField));
    }

    private static List<(string Field, string Rule)> Validate(TempLedger ledger) =>
        [.. new ValidateLedger(new YamlLedgerReader()).Run(ledger.Path, []).Errors.Select(error => (FieldOf(error.Message), error.Rule))];

    private static string FieldOf(string message) => FieldName().Match(message).Groups[1].Value;

    private static TempLedger Ledger(ReviewCandidate candidate, string? title)
    {
        var body = ReviewIssueRenderer.Render(candidate).Body;
        var files = OfferedFile().Matches(body).ToDictionary(match => match.Groups["path"].Value, match => match.Groups["yaml"].Value);
        files.Count.ShouldBe(2);

        var ledger = new TempLedger().Write("jurisdictions/guilford.yaml", """
            id: guilford
            name: Guilford County
            platform: legistar
            legistar_client: guilford
            bodies:
              - Board of Commissioners
            """);
        foreach (var (path, yaml) in files)
        {
            var content = title is null ? yaml : yaml.Replace("title: \"\"", $"title: \"{title}\"", StringComparison.Ordinal);
            ledger.Write(path["data/".Length..], content);
        }

        return ledger;
    }

    private static ReviewCandidate Candidate() => new()
    {
        JurisdictionId = "guilford",
        JurisdictionName = "Guilford County",
        Meeting = new MeetingKey(Platform.Legistar, "guilford", "1599"),
        Body = "Board of Commissioners",
        Date = new DateOnly(2026, 11, 5),
        Time = new TimeOnly(17, 30),
        StartsAt = new DateTimeOffset(2026, 11, 5, 17, 30, 0, TimeSpan.FromHours(-5)),
        Agenda = new AgendaDocument(
            Sha256Hash.Compute("agenda"u8),
            new Uri("https://guilford.legistar1.com/guilford/meetings/2026/11/1599_A_Board_of_Commissioners_Meeting_Agenda.pdf"),
            "application/pdf",
            123_456,
            new DateTimeOffset(2026, 10, 28, 12, 47, 3, TimeSpan.Zero)),
        Items = [new ReviewItem("A.", "2026-439", "Public Hearing", DataCenterRule)],
        FirstSeenUtc = new DateTimeOffset(2026, 10, 28, 12, 47, 3, TimeSpan.Zero),
    };

    [GeneratedRegex("`(?<path>data/[a-z]+/[a-z0-9-]+\\.yaml)`\\n\\n```yaml\\n(?<yaml>.*?)```", RegexOptions.Singleline)]
    private static partial Regex OfferedFile();

    [GeneratedRegex("'([a-z_]+)'")]
    private static partial Regex FieldName();
}
