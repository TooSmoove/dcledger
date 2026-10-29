using DcLedger.Publishing.Tabular;
using DcLedger.Testing;
using Shouldly;
using Xunit;
using static VerifyXunit.Verifier;

namespace DcLedger.Publishing.Tests;

public sealed class TabularExporterTests
{
    private static readonly string[] Header =
    [
        "id", "uid", "jurisdiction_id", "body", "title", "case_number", "starts_utc", "starts_local", "status", "sequence",
        "label", "document_urls", "document_retrieved_utc", "document_sha256",
    ];

    private static string EventsCsv() =>
        TestFiles.Text(TestFiles.Single(new TabularExporter().Export(ExportSamples.Model()), ExportPaths.EventsCsv));

    // Verify compares text with LF line endings; CRLF is asserted below.
    [Fact]
    public Task Events_csv_golden() => Verify(EventsCsv().ReplaceLineEndings("\n"));

    [Fact]
    public void Records_end_in_crlf_and_there_is_no_bom()
    {
        var csv = EventsCsv();

        csv[0].ShouldBe('i'); // the header, no BOM yet (#31 adds it)
        csv.EndsWith("\r\n", StringComparison.Ordinal).ShouldBeTrue();
        csv.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\n', StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Rows_round_trip_in_start_order_with_quoting()
    {
        var rows = Rfc4180.Parse(EventsCsv());

        rows[0].ShouldBe(Header);
        rows.Count.ShouldBe(4);
        rows.Skip(1).Select(row => row[0]).ShouldBe(
            [ExportSamples.October15.Id, ExportSamples.November3.Id, ExportSamples.November5.Id]);

        var october15 = rows[1];
        october15[1].ShouldBe(ExportSamples.October15.Uid);
        october15[4].ShouldBe(ExportSamples.October15.Title);
        october15[5].ShouldBe("Z-26-01");
        october15[8].ShouldBe("cancelled");
        october15[9].ShouldBe("1");
        october15[11].ShouldBe("https://news.example.org/2026/10/fixture-county-hearing https://fixture-county.example.org/agendas/1599.pdf");
        october15[12].ShouldBe("2026-10-11T09:30:00Z 2026-10-10T12:00:00Z");
        october15[13].ShouldBe($"{ExportSamples.NewsStory.Id.Value} {ExportSamples.CountyAgenda1599.Id.Value}");

        rows[2][4].ShouldBe(ExportSamples.November3.Title);
        rows[3][5].ShouldBe(string.Empty);
    }

    [Fact]
    public void Times_cross_the_daylight_saving_change_correctly()
    {
        var rows = Rfc4180.Parse(EventsCsv());

        (rows[1][6], rows[1][7]).ShouldBe(("2026-10-15T21:30:00Z", "2026-10-15T17:30:00-04:00"));
        (rows[3][6], rows[3][7]).ShouldBe(("2026-11-05T22:30:00Z", "2026-11-05T17:30:00-05:00"));
    }

    [Fact]
    public void An_empty_ledger_gives_just_the_header()
    {
        var model = ExportSamples.Model() with { Events = [] };

        var csv = TestFiles.Text(new TabularExporter().Export(model)[0]);

        Rfc4180.Parse(csv).ShouldHaveSingleItem().ShouldBe(Header);
    }
}
