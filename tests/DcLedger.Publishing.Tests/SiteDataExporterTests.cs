using System.Text.Json;
using DcLedger.Publishing.SiteData;
using DcLedger.Testing;
using Shouldly;
using Xunit;
using static VerifyXunit.Verifier;

namespace DcLedger.Publishing.Tests;

public sealed class SiteDataExporterTests
{
    private static readonly string[] LatLon = ["lat", "lon"];

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
        jurisdictions[1].GetProperty("stale").GetBoolean().ShouldBeFalse();
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
}
