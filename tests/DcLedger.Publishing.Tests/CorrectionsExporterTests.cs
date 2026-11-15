using System.Text.Json;
using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Publishing.SiteData;
using DcLedger.Testing;
using Shouldly;
using Xunit;

namespace DcLedger.Publishing.Tests;

/// <summary>
/// <c>site-data/corrections.json</c> (issue #117): the corrections log the methodology page lists, and the .NET half
/// of its contract with <c>site/tests/fixtures/corrections.json</c>.
/// </summary>
public sealed class CorrectionsExporterTests
{
    private static readonly string[] EntryKeys = ["id", "reported_on", "corrected_on", "record_ids", "summary", "issue"];

    [Fact]
    public void Writes_one_site_data_file_and_nothing_public()
    {
        var file = new CorrectionsExporter().Export(ExportSamples.ModelWithCorrections()).ShouldHaveSingleItem();

        file.Path.ShouldBe(ExportPaths.SiteCorrections);
        file.Path.ShouldStartWith("site-data/"); // the log is a page, not a download (SPEC §D)
    }

    [Fact]
    public void States_the_schema_version_and_the_day_the_log_opened()
    {
        using var document = Parse(ExportSamples.ModelWithCorrections());

        document.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["schema_version", "opened_on", "corrections"]);
        document.RootElement.GetProperty("schema_version").GetInt32().ShouldBe(CorrectionsExporter.SchemaVersion);
        document.RootElement.GetProperty("opened_on").GetString().ShouldBe("2026-11-22");
    }

    [Fact]
    public void Lists_entries_oldest_first_with_every_key_in_order()
    {
        using var document = Parse(ExportSamples.ModelWithCorrections());
        var entries = document.RootElement.GetProperty("corrections").EnumerateArray().ToList();

        entries.Select(e => e.GetProperty("id").GetString()!).ShouldBe([ExportSamples.WordingCorrection.Id, ExportSamples.VoteCorrection.Id]);
        foreach (var entry in entries)
        {
            entry.EnumerateObject().Select(p => p.Name).ShouldBe(EntryKeys);
        }

        var vote = entries[1];
        vote.GetProperty("reported_on").GetString().ShouldBe("2026-11-23");
        vote.GetProperty("corrected_on").GetString().ShouldBe("2026-11-24");
        vote.GetProperty("record_ids").EnumerateArray().Select(e => e.GetString()!).ShouldBe(ExportSamples.VoteCorrection.RecordIds);
        vote.GetProperty("summary").GetString().ShouldBe(ExportSamples.VoteCorrection.Summary, "quotes and markup survive as data");
        vote.GetProperty("issue").GetInt32().ShouldBe(131);
    }

    [Fact]
    public void An_entry_without_records_or_an_issue_has_an_empty_list_and_null()
    {
        using var document = Parse(ExportSamples.ModelWithCorrections());
        var wording = document.RootElement.GetProperty("corrections")[0];

        wording.GetProperty("record_ids").GetArrayLength().ShouldBe(0);
        wording.GetProperty("issue").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void An_empty_log_still_writes_the_file()
    {
        // The page needs the opening line from the first build, before any correction exists.
        using var document = Parse(ExportSamples.Model());

        document.RootElement.GetProperty("corrections").GetArrayLength().ShouldBe(0);
        document.RootElement.GetProperty("opened_on").GetString().ShouldBe("2026-11-22");
    }

    [Fact]
    public void Output_is_lf_terminated_utf8_without_a_bom()
    {
        var bytes = new CorrectionsExporter().Export(ExportSamples.ModelWithCorrections())[0].Content.ToArray();

        bytes.Take(3).ShouldNotBe("﻿"u8.ToArray());
        bytes[^1].ShouldBe((byte)'\n');
        bytes.ShouldNotContain((byte)'\r');
    }

    [Fact]
    public void Exported_corrections_json_has_the_shape_of_the_site_fixture()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(TestFiles.RepositoryPath("site", "tests", "fixtures", "corrections.json")));
        using var exported = Parse(ExportSamples.ModelWithCorrections());

        fixture.RootElement.GetProperty("schema_version").GetInt32().ShouldBe(CorrectionsExporter.SchemaVersion);
        fixture.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(exported.RootElement.EnumerateObject().Select(p => p.Name));
        var fixtureEntries = fixture.RootElement.GetProperty("corrections").EnumerateArray().ToList();
        fixtureEntries.ShouldNotBeEmpty("the site's tests need at least one entry to render");
        foreach (var entry in fixtureEntries)
        {
            entry.EnumerateObject().Select(p => p.Name).ShouldBe(EntryKeys);
            entry.GetProperty("record_ids").ValueKind.ShouldBe(JsonValueKind.Array);
            (entry.GetProperty("issue").ValueKind is JsonValueKind.Number or JsonValueKind.Null).ShouldBeTrue();
        }
    }

    [Fact]
    public void The_log_opens_on_launch_day()
    {
        Correction.LogOpenedOn.ShouldBe(new DateOnly(2026, 11, 22), "issue #117: \"Log opened Nov 22, 2026.\"");
    }

    private static JsonDocument Parse(ExportModel model) =>
        JsonDocument.Parse(TestFiles.Text(new CorrectionsExporter().Export(model)[0]));
}
