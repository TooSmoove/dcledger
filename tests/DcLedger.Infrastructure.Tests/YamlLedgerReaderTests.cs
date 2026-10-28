using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Infrastructure.Ledger;
using Shouldly;
using Xunit;

namespace DcLedger.Infrastructure.Tests;

public sealed class YamlLedgerReaderTests
{
    private const string Doc = "bf771d2f48e40c8ff1c60d5b2a63fc3680ba510fb30d8e9fa1e4fb069d163811";

    private const string ValidEvent = """
        id: fixture-boc-2026-11-05
        jurisdiction_id: fixture
        platform: legistar
        platform_meeting_id: "1599"
        body: Board of Commissioners
        title: "Fixture: regular meeting"
        starts_at: 2026-11-05T17:30:00-05:00
        status: scheduled
        sequence: 0
        label: confirmed_official
        document_ids:
          - bf771d2f48e40c8ff1c60d5b2a63fc3680ba510fb30d8e9fa1e4fb069d163811
        """;

    [Fact]
    public void Valid_fixture_ledger_loads_every_record_type()
    {
        var snapshot = new YamlLedgerReader().Load(FixturePath("valid"));

        snapshot.LoadErrors.ShouldBeEmpty();
        snapshot.Jurisdictions.ShouldHaveSingleItem().Record.Bodies.ShouldHaveSingleItem().ShouldBe("Board of Commissioners");
        snapshot.Documents.ShouldHaveSingleItem().Record.Id.Value.ShouldBe(Doc);
        snapshot.Actions.ShouldHaveSingleItem().Record.DecidedOn.ShouldBe(new DateOnly(2026, 1, 15));
        var loaded = snapshot.Events.ShouldHaveSingleItem();
        loaded.File.ShouldBe("events/fixture-county-boc-2026-11-05.yaml");
        loaded.Record.StartsAt.ShouldBe(new DateTimeOffset(2026, 11, 5, 17, 30, 0, TimeSpan.FromHours(-5)));
        snapshot.RecordFiles.Count.ShouldBe(4);
    }

    [Fact]
    public void Values_are_read_as_written_without_yaml_implicit_typing()
    {
        var yaml = ValidEvent.Replace("platform_meeting_id: \"1599\"", "platform_meeting_id: 0123", StringComparison.Ordinal)
            + "\ncase_number: no";

        var loaded = Load("events/fixture-boc-2026-11-05.yaml", yaml).Events.ShouldHaveSingleItem().Record;

        loaded.PlatformMeetingId.ShouldBe("0123");
        loaded.CaseNumber.ShouldBe("no");
    }

    [Theory]
    [InlineData("id: [unclosed", "YAML syntax error")]
    [InlineData("a: 1\n---\nb: 2", "two documents")]
    [InlineData("- just\n- a list", "a list instead of a mapping")]
    [InlineData("id: &anchor fixture\nname: *anchor", "anchors and aliases")]
    [InlineData("id: fixture\nid: other", "duplicate keys")]
    public void Malformed_file_is_a_parse_error(string yaml, string because)
    {
        RuleFor("events/fixture.yaml", yaml).ShouldBe(ValidationRule.ParseError, because);
    }

    [Fact]
    public void Non_yaml_file_in_a_record_folder_is_a_parse_error()
    {
        var snapshot = Load("events/fixture.yml", ValidEvent);

        snapshot.LoadErrors.ShouldHaveSingleItem().Rule.ShouldBe(ValidationRule.ParseError);
        snapshot.RecordFiles.ShouldBeEmpty();
    }

    [Fact]
    public void File_over_64_kb_is_rejected_unread()
    {
        RuleFor("events/fixture.yaml", "# " + new string('x', YamlLedgerReader.MaxFileBytes)).ShouldBe(ValidationRule.FileTooLarge);
    }

    [Fact]
    public void Unknown_key_is_reported()
    {
        RuleFor("events/fixture-boc-2026-11-05.yaml", ValidEvent + "\ncolor: blue").ShouldBe(ValidationRule.UnknownField);
    }

    [Theory]
    [InlineData("original_title")]
    [InlineData("petitioner_name")]
    [InlineData("owner_name")]
    public void Personal_or_original_title_key_is_forbidden(string key)
    {
        RuleFor("events/fixture-boc-2026-11-05.yaml", ValidEvent + $"\n{key}: Someone").ShouldBe(ValidationRule.ForbiddenField);
    }

    [Fact]
    public void Missing_required_key_is_reported()
    {
        var yaml = ValidEvent.Replace("title: \"Fixture: regular meeting\"\n", string.Empty, StringComparison.Ordinal);

        RuleFor("events/fixture-boc-2026-11-05.yaml", yaml).ShouldBe(ValidationRule.RequiredField);
    }

    [Theory]
    [InlineData("status: scheduled", "status: maybe")]
    [InlineData("id: fixture-boc-2026-11-05", "id: Fixture_BOC")]
    [InlineData("sequence: 0", "sequence: -1")]
    [InlineData("starts_at: 2026-11-05T17:30:00-05:00", "starts_at: next Thursday")]
    [InlineData("  - bf771d2f48e40c8ff1c60d5b2a63fc3680ba510fb30d8e9fa1e4fb069d163811", "  - not-a-hash")]
    public void Badly_formatted_value_is_reported(string original, string replacement)
    {
        var yaml = ValidEvent.Replace(original, replacement, StringComparison.Ordinal);

        RuleFor("events/fixture-boc-2026-11-05.yaml", yaml).ShouldBe(ValidationRule.InvalidValue);
    }

    [Fact]
    public void Meeting_time_without_offset_is_reported()
    {
        var yaml = ValidEvent.Replace("2026-11-05T17:30:00-05:00", "2026-11-05T17:30:00", StringComparison.Ordinal);

        RuleFor("events/fixture-boc-2026-11-05.yaml", yaml).ShouldBe(ValidationRule.OffsetMissing);
    }

    [Fact]
    public void Document_url_must_be_http_or_https()
    {
        var yaml = $"""
            id: {Doc}
            url: ftp://example.org/agenda.pdf
            retrieved_at: 2026-10-25T21:17:05Z
            media_type: application/pdf
            bytes: 27
            source_type: official
            """;

        RuleFor($"documents/{Doc}.yaml", yaml).ShouldBe(ValidationRule.InvalidValue);
    }

    [Fact]
    public void Retrieval_time_must_be_utc()
    {
        var yaml = $"""
            id: {Doc}
            url: https://example.org/agenda.pdf
            retrieved_at: 2026-10-25T17:17:05-04:00
            media_type: application/pdf
            bytes: 27
            source_type: official
            """;

        RuleFor($"documents/{Doc}.yaml", yaml).ShouldBe(ValidationRule.InvalidValue);
    }

    [Fact]
    public void Broken_file_is_still_listed_as_a_record_file()
    {
        var snapshot = Load("events/fixture.yaml", "id: [unclosed");

        snapshot.RecordFiles.ShouldHaveSingleItem().ShouldBe("events/fixture.yaml");
        snapshot.Events.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("events/x.yaml", true)]
    [InlineData("jurisdictions/guilford.yaml", true)]
    [InlineData("events/x.yml", false)]
    [InlineData("README.md", false)]
    [InlineData("other/x.yaml", false)]
    [InlineData("events/nested/x.yaml", false)]
    public void Record_paths_are_recognized(string path, bool expected)
    {
        YamlLedgerReader.IsRecordPath(path).ShouldBe(expected);
    }

    private static LedgerSnapshot Load(string relativePath, string yaml)
    {
        using var ledger = new TempLedger().Write(relativePath, yaml);
        return new YamlLedgerReader().Load(ledger.Path);
    }

    private static string RuleFor(string relativePath, string yaml) =>
        Load(relativePath, yaml).LoadErrors.ShouldHaveSingleItem().Rule;

    private static string FixturePath(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DcLedger.slnx")))
            {
                return Path.Combine(directory.FullName, "tests", "fixtures", "ledger", name);
            }
        }

        throw new InvalidOperationException("DcLedger.slnx not found above " + AppContext.BaseDirectory);
    }
}
