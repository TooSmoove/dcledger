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

    private const string ValidAction = """
        id: fixture-moratorium-2026-11-05
        jurisdiction_id: fixture
        event_id: fixture-boc-2026-11-05
        action_type: moratorium
        outcome: adopted_amended
        decided_on: 2026-11-05
        votes: "9-0"
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
        var action = snapshot.Actions.ShouldHaveSingleItem().Record;
        action.DecidedOn.ShouldBe(new DateOnly(2026, 1, 15));
        action.Terms.ShouldBe(new DecisionTerms
        {
            InstrumentNumber = "26-01",
            Threshold = "electrical power demand exceeding 10 megawatts",
            Scope = "acceptance, processing and approval of development applications for data center facilities",
            EffectiveOn = new DateOnly(2026, 1, 15),
            ExpiresOn = new DateOnly(2026, 7, 14),
            EarlyTermination = "upon adoption of amendments to the development ordinance, whichever occurs first",
            Exemptions = "applications deemed complete before the effective date",
            Status = ActionStatus.InEffect,
        }, "every term is read as written; a quoted value that wraps is one line");
        snapshot.Jurisdictions.ShouldHaveSingleItem().Record.CoverageTier.ShouldBe(CoverageTier.Watched);
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

    [Theory]
    [InlineData("location:\n  lat: 36.0726\n  lon: -79.792", 36.0726, -79.792)]
    [InlineData("location: { lat: 36.072611, lon: -79.79 }", 36.072611, -79.79)]
    [InlineData("location: { lat: \"36\", lon: \"-80\" }", 36.0, -80.0)]
    public void Location_is_read_as_decimal_degrees(string location, double latitude, double longitude)
    {
        var loaded = Load("events/fixture-boc-2026-11-05.yaml", ValidEvent + "\n" + location).Events.ShouldHaveSingleItem().Record;

        loaded.Location.ShouldBe(new Location { Latitude = latitude, Longitude = longitude });
    }

    [Fact]
    public void Location_is_optional()
    {
        Load("events/fixture-boc-2026-11-05.yaml", ValidEvent).Events.ShouldHaveSingleItem().Record.Location.ShouldBeNull();
    }

    [Theory]
    [InlineData("location: 36.0726, -79.792", "not a mapping")]
    [InlineData("location: { lat: 36.0726 }", "lon missing")]
    [InlineData("location: { lat: 3.6e1, lon: -79.792 }", "exponent")]
    [InlineData("location: { lat: 36.0726123, lon: -79.792 }", "more than 6 decimals")]
    [InlineData("location: { lat: +36.07, lon: -79.792 }", "plus sign")]
    [InlineData("location: { lat: '', lon: -79.792 }", "blank")]
    public void Malformed_location_is_reported(string location, string because)
    {
        var errors = Load("events/fixture-boc-2026-11-05.yaml", ValidEvent + "\n" + location).LoadErrors;

        errors.ShouldNotBeEmpty(because);
        errors.ShouldAllBe(error => error.Rule == ValidationRule.InvalidValue || error.Rule == ValidationRule.RequiredField || error.Rule == ValidationRule.UnknownField, because);
        errors.ShouldAllBe(error => error.RecordId == "fixture-boc-2026-11-05", because);
    }

    [Theory]
    [InlineData("name: Example Road site", ValidationRule.UnknownField)]
    [InlineData("address: 123 Example Rd", ValidationRule.UnknownField)]
    [InlineData("owner: Jane Resident", ValidationRule.ForbiddenField)]
    public void Location_holds_only_a_point(string extra, string rule)
    {
        var yaml = ValidEvent + "\nlocation:\n  lat: 36.0726\n  lon: -79.792\n  " + extra;

        RuleFor("events/fixture-boc-2026-11-05.yaml", yaml).ShouldBe(rule);
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

    [Theory]
    [InlineData("", Retrieval.Fetched)]
    [InlineData("retrieval: fetched\n", Retrieval.Fetched)]
    [InlineData("retrieval: manual\n", Retrieval.Manual)]
    public void Document_retrieval_is_fetched_unless_it_says_manual(string line, Retrieval expected)
    {
        var yaml = $"""
            id: {Doc}
            url: https://example.org/agenda.pdf
            retrieved_at: 2026-10-25T21:17:05Z
            media_type: application/pdf
            bytes: 27
            source_type: official

            """ + line;

        Load($"documents/{Doc}.yaml", yaml).Documents.ShouldHaveSingleItem().Record.Retrieval.ShouldBe(expected);
    }

    [Fact]
    public void Document_retrieval_outside_the_vocabulary_is_rejected()
    {
        var yaml = $"""
            id: {Doc}
            url: https://example.org/agenda.pdf
            retrieved_at: 2026-10-25T21:17:05Z
            media_type: application/pdf
            bytes: 27
            source_type: official
            retrieval: browser
            """;

        RuleFor($"documents/{Doc}.yaml", yaml).ShouldBe(ValidationRule.InvalidValue);
    }

    [Fact]
    public void Action_reads_its_meeting_type_outcome_and_vote()
    {
        var action = Load("actions/fixture-moratorium-2026-11-05.yaml", ValidAction).Actions.ShouldHaveSingleItem().Record;

        action.EventId.ShouldBe("fixture-boc-2026-11-05");
        action.ActionType.ShouldBe(ActionType.Moratorium);
        action.Outcome.ShouldBe(ActionOutcome.AdoptedAmended);
        action.DecidedOn.ShouldBe(new DateOnly(2026, 11, 5));
        action.Votes.ShouldBe("9-0");
    }

    [Fact]
    public void Action_without_a_meeting_or_vote_is_valid()
    {
        var yaml = ValidAction
            .Replace("event_id: fixture-boc-2026-11-05\n", string.Empty, StringComparison.Ordinal)
            .Replace("votes: \"9-0\"\n", string.Empty, StringComparison.Ordinal);

        var action = Load("actions/fixture-moratorium-2026-11-05.yaml", yaml).Actions.ShouldHaveSingleItem().Record;

        action.EventId.ShouldBeNull();
        action.Votes.ShouldBeNull();
        action.Unanimous.ShouldBeFalse();
        action.Sequence.ShouldBe(0, "records written before #90 have no sequence");
    }

    [Fact]
    public void Action_reads_its_sequence()
    {
        var yaml = ValidAction.Replace("label: confirmed_official\n", "sequence: 2\nlabel: confirmed_official\n", StringComparison.Ordinal);

        Load("actions/fixture-moratorium-2026-11-05.yaml", yaml).Actions.ShouldHaveSingleItem().Record.Sequence.ShouldBe(2);
    }

    [Theory]
    [InlineData("sequence: -1\n")]
    [InlineData("sequence: two\n")]
    [InlineData("sequence: 1.5\n")]
    public void Action_sequence_is_a_whole_number(string line)
    {
        var yaml = ValidAction.Replace("label: confirmed_official\n", line + "label: confirmed_official\n", StringComparison.Ordinal);

        RuleFor("actions/fixture-moratorium-2026-11-05.yaml", yaml).ShouldBe(ValidationRule.InvalidValue);
    }

    [Fact]
    public void Action_can_be_unanimous_before_the_tally_is_posted()
    {
        var yaml = ValidAction.Replace("votes: \"9-0\"\n", "unanimous: true\n", StringComparison.Ordinal);

        var action = Load("actions/fixture-moratorium-2026-11-05.yaml", yaml).Actions.ShouldHaveSingleItem().Record;

        action.Votes.ShouldBeNull();
        action.Unanimous.ShouldBeTrue();
    }

    [Theory]
    [InlineData("9-0")]
    [InlineData("8-00")]
    public void Action_can_be_unanimous_with_a_tally_that_has_no_noes(string votes)
    {
        var yaml = ValidAction.Replace("votes: \"9-0\"\n", $"votes: \"{votes}\"\nunanimous: true\n", StringComparison.Ordinal);

        var action = Load("actions/fixture-moratorium-2026-11-05.yaml", yaml).Actions.ShouldHaveSingleItem().Record;

        action.Votes.ShouldBe(votes);
        action.Unanimous.ShouldBeTrue();
    }

    [Theory]
    [InlineData("votes: \"9-0\"\n", "unanimous: false\n", "false is left out: a split vote is a tally")]
    [InlineData("votes: \"9-0\"\n", "unanimous: yes\n", "only the literal true")]
    [InlineData("votes: \"9-0\"\n", "unanimous: True\n", "lowercase only")]
    [InlineData("votes: \"9-0\"\n", "votes: \"8-1\"\nunanimous: true\n", "a unanimous vote has no noes")]
    public void Unanimous_must_be_true_and_agree_with_the_tally(string valid, string invalid, string because)
    {
        var yaml = ValidAction.Replace(valid, invalid, StringComparison.Ordinal);

        RuleFor("actions/fixture-moratorium-2026-11-05.yaml", yaml).ShouldBe(ValidationRule.InvalidValue, because);
    }

    [Theory]
    [InlineData("action_type: moratorium", "action_type: pause", "unknown action type")]
    [InlineData("outcome: adopted_amended", "outcome: approved", "unknown outcome")]
    [InlineData("outcome: adopted_amended", "outcome: Adopted", "tokens are lowercase")]
    [InlineData("votes: \"9-0\"", "votes: unanimous", "a description instead of a count")]
    [InlineData("votes: \"9-0\"", "votes: \"9 to 0\"", "words instead of a hyphen")]
    [InlineData("votes: \"9-0\"", "votes: \"9-0-1\"", "abstentions are left out")]
    [InlineData("event_id: fixture-boc-2026-11-05", "event_id: Fixture_BOC", "event_id isn't a slug")]
    public void Action_values_outside_the_vocabulary_are_rejected(string valid, string invalid, string because)
    {
        var yaml = ValidAction.Replace(valid, invalid, StringComparison.Ordinal);

        RuleFor("actions/fixture-moratorium-2026-11-05.yaml", yaml).ShouldBe(ValidationRule.InvalidValue, because);
    }

    [Fact]
    public void Action_needs_an_outcome()
    {
        var yaml = ValidAction.Replace("outcome: adopted_amended\n", string.Empty, StringComparison.Ordinal);

        RuleFor("actions/fixture-moratorium-2026-11-05.yaml", yaml).ShouldBe(ValidationRule.RequiredField);
    }

    [Fact]
    public void Action_without_terms_has_none()
    {
        Load("actions/fixture-moratorium-2026-11-05.yaml", ValidAction).Actions.ShouldHaveSingleItem().Record.Terms.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Action_reads_its_next_step_date()
    {
        var action = Load("actions/fixture-moratorium-2026-11-05.yaml", ValidAction + "\nnext_on: 2026-12-03").Actions.ShouldHaveSingleItem().Record;

        action.NextOn.ShouldBe(new DateOnly(2026, 12, 3));
    }

    [Fact]
    public void Action_without_next_on_has_none()
    {
        Load("actions/fixture-moratorium-2026-11-05.yaml", ValidAction).Actions.ShouldHaveSingleItem().Record.NextOn.ShouldBeNull();
    }

    [Fact]
    public void Action_reads_its_successor_and_status()
    {
        var yaml = ValidAction + "\nsuccessor_id: fixture-moratorium-2027-05-04\nstatus: superseded";

        var terms = Load("actions/fixture-moratorium-2026-11-05.yaml", yaml).Actions.ShouldHaveSingleItem().Record.Terms;

        terms.SuccessorId.ShouldBe("fixture-moratorium-2027-05-04");
        terms.Status.ShouldBe(ActionStatus.Superseded);
    }

    [Theory]
    [InlineData("in_effect", ActionStatus.InEffect)]
    [InlineData("expired", ActionStatus.Expired)]
    [InlineData("superseded", ActionStatus.Superseded)]
    [InlineData("repealed", ActionStatus.Repealed)]
    public void Action_status_takes_its_four_tokens(string token, ActionStatus expected)
    {
        var yaml = ValidAction + "\nstatus: " + token;

        Load("actions/fixture-moratorium-2026-11-05.yaml", yaml).Actions.ShouldHaveSingleItem().Record.Terms.Status.ShouldBe(expected);
    }

    [Theory]
    [InlineData("instrument_number")]
    [InlineData("threshold")]
    [InlineData("scope")]
    [InlineData("early_termination")]
    [InlineData("exemptions")]
    public void A_text_term_is_one_line_of_at_most_500_characters(string key)
    {
        var atTheCap = new string('x', DecisionTerms.MaxTextLength);

        Load("actions/fixture-moratorium-2026-11-05.yaml", $"{ValidAction}\n{key}: {atTheCap}").LoadErrors.ShouldBeEmpty("exactly at the cap");
        RuleFor("actions/fixture-moratorium-2026-11-05.yaml", $"{ValidAction}\n{key}: {atTheCap}x").ShouldBe(ValidationRule.InvalidValue, "one over the cap");
        RuleFor("actions/fixture-moratorium-2026-11-05.yaml", $"{ValidAction}\n{key}: \"\"").ShouldBe(ValidationRule.InvalidValue, "empty: leave the key out");
        RuleFor("actions/fixture-moratorium-2026-11-05.yaml", $"{ValidAction}\n{key}: |\n  first line\n  second line").ShouldBe(ValidationRule.InvalidValue, "a block scalar keeps its line breaks");
        RuleFor("actions/fixture-moratorium-2026-11-05.yaml", $"{ValidAction}\n{key}: \"tab\\there\"").ShouldBe(ValidationRule.InvalidValue, "a tab");
        RuleFor("actions/fixture-moratorium-2026-11-05.yaml", $"{ValidAction}\n{key}: [a, b]").ShouldBe(ValidationRule.InvalidValue, "a list");
    }

    [Theory]
    [InlineData("effective_on: 11/05/2026", "not an ISO date")]
    [InlineData("expires_on: 2027-02-30", "no such day")]
    [InlineData("expires_on: 2027-02-14T00:00:00Z", "a time, not a date")]
    [InlineData("successor_id: Fixture_Moratorium", "successor_id isn't a slug")]
    [InlineData("status: active", "unknown status")]
    [InlineData("status: In_Effect", "tokens are lowercase")]
    [InlineData("next_on: 12/03/2026", "next_on isn't an ISO date")]
    [InlineData("next_on: 2026-12-03T19:00:00-05:00", "next_on is a date, not a time")]
    public void Malformed_term_values_are_rejected(string line, string because)
    {
        RuleFor("actions/fixture-moratorium-2026-11-05.yaml", ValidAction + "\n" + line).ShouldBe(ValidationRule.InvalidValue, because);
    }

    [Theory]
    [InlineData("watched", CoverageTier.Watched)]
    [InlineData("decisions", CoverageTier.Decisions)]
    [InlineData("registered", CoverageTier.Registered)]
    public void Jurisdiction_reads_its_coverage_tier(string token, CoverageTier expected)
    {
        var yaml = $"id: fixture\nname: Fixture County\nplatform: manual\ncoverage_tier: {token}";

        Load("jurisdictions/fixture.yaml", yaml).Jurisdictions.ShouldHaveSingleItem().Record.CoverageTier.ShouldBe(expected);
    }

    [Fact]
    public void Jurisdiction_needs_a_coverage_tier()
    {
        RuleFor("jurisdictions/fixture.yaml", "id: fixture\nname: Fixture County\nplatform: manual").ShouldBe(ValidationRule.RequiredField);
    }

    [Theory]
    [InlineData("coverage_tier: full")]
    [InlineData("coverage_tier: Watched")]
    public void Coverage_tier_outside_the_vocabulary_is_rejected(string line)
    {
        RuleFor("jurisdictions/fixture.yaml", "id: fixture\nname: Fixture County\nplatform: manual\n" + line).ShouldBe(ValidationRule.InvalidValue);
    }

    private const string NotedJurisdiction = "id: fixture\nname: Fixture County\nplatform: manual\ncoverage_tier: decisions";

    [Fact]
    public void Jurisdiction_reads_its_note_and_the_documents_behind_it()
    {
        var yaml = $"{NotedJurisdiction}\nnote: \"A court order: \\\"judgment\\\".\"\nnote_document_ids:\n  - {Doc}";

        var jurisdiction = Load("jurisdictions/fixture.yaml", yaml).Jurisdictions.ShouldHaveSingleItem().Record;

        jurisdiction.Note.ShouldBe("A court order: \"judgment\".");
        jurisdiction.NoteDocumentIds.ShouldHaveSingleItem().Value.ShouldBe(Doc);
    }

    [Fact]
    public void Jurisdiction_note_is_optional()
    {
        var jurisdiction = Load("jurisdictions/fixture.yaml", NotedJurisdiction).Jurisdictions.ShouldHaveSingleItem().Record;

        jurisdiction.Note.ShouldBeNull();
        jurisdiction.NoteDocumentIds.ShouldBeEmpty();
    }

    [Fact]
    public void Jurisdiction_note_is_one_line_of_at_most_500_characters()
    {
        var atTheCap = new string('x', Jurisdiction.MaxNoteLength);

        Load("jurisdictions/fixture.yaml", $"{NotedJurisdiction}\nnote: {atTheCap}").LoadErrors.ShouldBeEmpty("exactly at the cap");
        RuleFor("jurisdictions/fixture.yaml", $"{NotedJurisdiction}\nnote: {atTheCap}x").ShouldBe(ValidationRule.InvalidValue, "one over the cap");
        RuleFor("jurisdictions/fixture.yaml", $"{NotedJurisdiction}\nnote: \"\"").ShouldBe(ValidationRule.InvalidValue, "empty: leave the key out");
        RuleFor("jurisdictions/fixture.yaml", $"{NotedJurisdiction}\nnote: |\n  first line\n  second line").ShouldBe(ValidationRule.InvalidValue, "a block scalar keeps its line breaks");
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
