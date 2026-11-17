using System.Text.Json;
using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Publishing.SiteData;
using DcLedger.Testing;
using Shouldly;
using Xunit;
using static VerifyXunit.Verifier;

namespace DcLedger.Publishing.Tests;

public sealed class SiteDataExporterTests
{
    private static readonly string[] LatLon = ["lat", "lon"];
    private static readonly string[] DocumentKeys = ["url", "retrieved_utc", "sha256", "retrieval", "kind", "thumbnail"];

    private static string EventsJson() =>
        TestFiles.Text(TestFiles.Single(new SiteDataExporter().Export(ExportSamples.Model()), ExportPaths.SiteData));

    [Fact]
    public Task Events_json_golden() => Verify(EventsJson());

    [Fact]
    public void Times_cross_the_daylight_saving_change_correctly()
    {
        var json = EventsJson();

        json.ShouldContain("\"starts_utc\": \"2026-10-15T21:30:00Z\"");
        json.ShouldContain("\"starts_local\": \"2026-10-15T17:30:00-04:00\"");
        json.ShouldContain("\"starts_utc\": \"2026-11-05T22:30:00Z\"");
        json.ShouldContain("\"starts_local\": \"2026-11-05T17:30:00-05:00\"");
    }

    [Fact]
    public void Output_is_lf_terminated_utf8_without_a_bom()
    {
        var json = EventsJson();

        json[0].ShouldBe('{'); // no BOM
        json.IndexOf('\r', StringComparison.Ordinal).ShouldBe(-1);
        json.ShouldEndWith("}\n");
    }

    [Fact]
    public void Freshness_and_provenance_are_present()
    {
        using var document = JsonDocument.Parse(EventsJson());
        var root = document.RootElement;

        root.GetProperty("schema_version").GetInt32().ShouldBe(SiteDataExporter.SchemaVersion);
        root.GetProperty("generated_utc").GetString().ShouldBe("2026-11-05T14:47:00Z");

        var jurisdictions = root.GetProperty("jurisdictions").EnumerateArray().ToList();
        jurisdictions.Select(j => j.GetProperty("id").GetString()).ShouldBe([ExportSamples.City.Id, ExportSamples.County.Id]);
        jurisdictions[0].GetProperty("stale").GetBoolean().ShouldBeTrue();
        jurisdictions[0].GetProperty("stale_after_hours").GetInt32().ShouldBe(168, "manual: 7 days");
        jurisdictions[1].GetProperty("stale").GetBoolean().ShouldBeFalse();
        jurisdictions[1].GetProperty("stale_after_hours").GetInt32().ShouldBe(48, "automated: 48 hours");
        jurisdictions[1].GetProperty("last_checked_utc").GetString().ShouldBe("2026-11-05T12:17:43Z");

        var october15 = root.GetProperty("events")[0];
        october15.GetProperty("uid").GetString().ShouldBe(ExportSamples.October15.Uid);
        october15.GetProperty("status").GetString().ShouldBe("cancelled");
        october15.GetProperty("title").GetString().ShouldBe(ExportSamples.October15.Title);
        var documents = october15.GetProperty("documents").EnumerateArray().ToList();
        documents.Select(d => d.GetProperty("sha256").GetString()).ShouldBe(
            [ExportSamples.NewsStory.Id.Value, ExportSamples.CountyAgenda1599.Id.Value]);
        documents[0].GetProperty("retrieved_utc").GetString().ShouldBe("2026-10-11T09:30:00Z");
        documents[0].GetProperty("url").GetString().ShouldBe("https://news.example.org/2026/10/fixture-county-hearing");
    }

    [Fact]
    public void Documents_say_how_they_were_retrieved()
    {
        using var document = JsonDocument.Parse(EventsJson());
        var events = document.RootElement.GetProperty("events");

        var saved = events[1].GetProperty("documents").EnumerateArray().ShouldHaveSingleItem();
        saved.GetProperty("sha256").GetString().ShouldBe(ExportSamples.CityAgenda.Id.Value);
        saved.GetProperty("retrieval").GetString().ShouldBe("manual");
        saved.EnumerateObject().Select(p => p.Name).ShouldBe(DocumentKeys); // retrieval, then kind and thumbnail, were added last, within schema version 1
        events[0].GetProperty("documents").EnumerateArray().Select(d => d.GetProperty("retrieval").GetString()).ShouldAllBe(retrieval => retrieval == "fetched");
        document.RootElement.GetProperty("actions")[0].GetProperty("documents")[0].GetProperty("retrieval").GetString().ShouldBe("fetched");
    }

    [Fact]
    public void Location_is_a_point_or_null()
    {
        using var document = JsonDocument.Parse(EventsJson());
        var events = document.RootElement.GetProperty("events");

        events[0].GetProperty("location").ValueKind.ShouldBe(JsonValueKind.Null);
        var location = events[2].GetProperty("location");
        location.GetProperty("lat").GetDouble().ShouldBe(36.0726);
        location.GetProperty("lon").GetDouble().ShouldBe(-79.792);
        location.EnumerateObject().Select(p => p.Name).ShouldBe(LatLon);
    }

    [Fact]
    public void Missing_case_numbers_and_last_checks_are_null()
    {
        var model = ExportSamples.Model() with
        {
            Jurisdictions = [ExportSamples.Model().Jurisdictions[0] with { LastCheckedUtc = null }],
        };
        using var document = JsonDocument.Parse(TestFiles.Text(new SiteDataExporter().Export(model)[0]));

        document.RootElement.GetProperty("jurisdictions")[0].GetProperty("last_checked_utc").ValueKind.ShouldBe(JsonValueKind.Null);
        document.RootElement.GetProperty("events")[2].GetProperty("case_number").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void Actions_carry_their_decision_meeting_and_provenance()
    {
        using var document = JsonDocument.Parse(EventsJson());

        var action = document.RootElement.GetProperty("actions").EnumerateArray().ShouldHaveSingleItem();
        action.GetProperty("id").GetString().ShouldBe(ExportSamples.November5Moratorium.Id);
        action.GetProperty("event_id").GetString().ShouldBe(ExportSamples.November5.Id);
        action.GetProperty("action_type").GetString().ShouldBe("moratorium");
        action.GetProperty("outcome").GetString().ShouldBe("adopted_amended");
        action.GetProperty("decided_on").GetString().ShouldBe("2026-11-05");
        action.GetProperty("votes").GetString().ShouldBe("8-1");
        action.GetProperty("unanimous").GetBoolean().ShouldBeFalse();
        action.GetProperty("sequence").GetInt32().ShouldBe(0);
        action.GetProperty("label").GetString().ShouldBe("confirmed_official");
        var source = action.GetProperty("documents").EnumerateArray().ShouldHaveSingleItem();
        source.GetProperty("sha256").GetString().ShouldBe(ExportSamples.CountyActionDetail1600.Id.Value);
        source.GetProperty("retrieved_utc").GetString().ShouldBe("2026-11-06T13:00:00Z");
    }

    [Fact]
    public void An_action_without_a_meeting_or_vote_writes_nulls()
    {
        var model = ExportSamples.Model();
        var bare = model.Actions[0] with { Action = ExportSamples.November5Moratorium with { EventId = null, Votes = null } };
        using var document = JsonDocument.Parse(TestFiles.Text(new SiteDataExporter().Export(model with { Actions = [bare] })[0]));

        var action = document.RootElement.GetProperty("actions")[0];
        action.GetProperty("event_id").ValueKind.ShouldBe(JsonValueKind.Null);
        action.GetProperty("votes").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void A_unanimous_action_without_a_tally_says_so()
    {
        var model = ExportSamples.Model();
        var reported = model.Actions[0] with { Action = ExportSamples.November5Moratorium with { Votes = null, Unanimous = true } };
        using var document = JsonDocument.Parse(TestFiles.Text(new SiteDataExporter().Export(model with { Actions = [reported] })[0]));

        var action = document.RootElement.GetProperty("actions")[0];
        action.GetProperty("votes").ValueKind.ShouldBe(JsonValueKind.Null);
        action.GetProperty("unanimous").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void A_ledger_without_actions_writes_an_empty_array()
    {
        using var document = JsonDocument.Parse(TestFiles.Text(new SiteDataExporter().Export(ExportSamples.Model() with { Actions = [] })[0]));

        document.RootElement.GetProperty("actions").GetArrayLength().ShouldBe(0);
    }

    private static readonly string[] TermKeys =
    [
        "instrument_number", "threshold", "scope", "effective_on", "expires_on", "early_termination", "exemptions",
        "successor_id", "status",
    ];

    private static readonly string[] JurisdictionTailKeys = ["coverage_tier", "note", "note_documents"];

    [Fact]
    public void Jurisdictions_end_with_their_coverage_tier_then_the_note()
    {
        using var document = JsonDocument.Parse(EventsJson());

        foreach (var jurisdiction in document.RootElement.GetProperty("jurisdictions").EnumerateArray())
        {
            jurisdiction.EnumerateObject().Select(p => p.Name).TakeLast(3).ToArray().ShouldBe(JurisdictionTailKeys, "new keys are appended (ADR-0004)");
            jurisdiction.GetProperty("coverage_tier").GetString().ShouldBe("watched");
            jurisdiction.GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
            jurisdiction.GetProperty("note_documents").GetArrayLength().ShouldBe(0);
        }
    }

    [Fact]
    public void A_jurisdiction_note_is_written_with_its_documents()
    {
        var snapshot = ExportSamples.Snapshot();
        var noted = ExportSamples.County with
        {
            Note = "Fixture court order: \"judgment for the plaintiff\".",
            NoteDocumentIds = [ExportSamples.NewsStory.Id],
        };
        var model = ExportModelBuilder.Build(
            snapshot with
            {
                Jurisdictions = [.. snapshot.Jurisdictions.Select(j => j.Record.Id == ExportSamples.County.Id ? j with { Record = noted } : j)],
            },
            ExportSamples.Status()).Model;
        using var document = JsonDocument.Parse(TestFiles.Text(new SiteDataExporter().Export(model)[0]));

        var county = document.RootElement.GetProperty("jurisdictions").EnumerateArray().Single(j => j.GetProperty("id").GetString() == ExportSamples.County.Id);
        county.GetProperty("note").GetString().ShouldBe(noted.Note);
        var sources = county.GetProperty("note_documents");
        sources.GetArrayLength().ShouldBe(1);
        sources[0].GetProperty("sha256").GetString().ShouldBe(ExportSamples.NewsStory.Id.Value);
        sources[0].EnumerateObject().Select(p => p.Name).ToArray().ShouldBe(DocumentKeys);
    }

    [Fact]
    public void A_jurisdiction_that_is_not_watched_writes_its_tier_and_no_last_check()
    {
        var model = ExportModelBuilder.Build(
            ExportSamples.Snapshot() with
            {
                Jurisdictions = [.. ExportSamples.Snapshot().Jurisdictions.Select(j => j.Record.Id == ExportSamples.City.Id ? j with { Record = ExportSamples.City with { CoverageTier = CoverageTier.Decisions } } : j)],
            },
            ExportSamples.Status()).Model;
        using var document = JsonDocument.Parse(TestFiles.Text(new SiteDataExporter().Export(model)[0]));

        var city = document.RootElement.GetProperty("jurisdictions").EnumerateArray().Single(j => j.GetProperty("id").GetString() == ExportSamples.City.Id);
        city.GetProperty("coverage_tier").GetString().ShouldBe("decisions");
        city.GetProperty("last_checked_utc").ValueKind.ShouldBe(JsonValueKind.Null);
        city.GetProperty("stale").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void An_action_without_terms_writes_each_term_key_as_null_after_its_documents()
    {
        using var document = JsonDocument.Parse(EventsJson());

        var action = document.RootElement.GetProperty("actions")[0];
        var keys = action.EnumerateObject().Select(p => p.Name).ToList();
        keys[^TermKeys.Length..].ShouldBe(TermKeys);
        keys[^(TermKeys.Length + 1)].ShouldBe("documents");
        TermKeys.ShouldAllBe(key => action.GetProperty(key).ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public void An_action_with_terms_writes_them_as_recorded()
    {
        using var document = JsonDocument.Parse(TestFiles.Text(new SiteDataExporter().Export(ExportSamples.ModelWithTerms())[0]));

        var action = document.RootElement.GetProperty("actions")[0];
        action.GetProperty("instrument_number").GetString().ShouldBe("=26-01");
        action.GetProperty("threshold").GetString().ShouldBe("+electrical power demand exceeding 10 megawatts");
        action.GetProperty("scope").GetString().ShouldBe("-development approvals for <data center> facilities & accessory uses", "JSON keeps text as it is");
        action.GetProperty("effective_on").GetString().ShouldBe("2026-11-05");
        action.GetProperty("expires_on").GetString().ShouldBe("2027-05-04");
        action.GetProperty("early_termination").GetString().ShouldBe("@upon adoption of text amendments, whichever occurs first");
        action.GetProperty("exemptions").GetString().ShouldBe("'applications deemed complete before the effective date");
        action.GetProperty("successor_id").GetString().ShouldBe("fixture-county-moratorium-2027-05-04");
        action.GetProperty("status").GetString().ShouldBe("superseded");
    }
}
