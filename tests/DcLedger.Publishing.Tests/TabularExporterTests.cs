using System.Diagnostics.CodeAnalysis;
using DcLedger.Publishing.Tabular;
using DcLedger.Testing;
using DuckDB.NET.Data;
using Shouldly;
using Xunit;
using static VerifyXunit.Verifier;

namespace DcLedger.Publishing.Tests;

public sealed class TabularExporterTests
{
    private static readonly string[] Header =
    [
        "id", "uid", "jurisdiction_id", "body", "title", "case_number", "starts_utc", "starts_local", "status", "sequence",
        "label", "location_lat", "location_lon", "document_urls", "document_retrieved_utc", "document_sha256",
    ];

    private static string EventsCsv() =>
        TestFiles.Text(TestFiles.Single(new TabularExporter().Export(ExportSamples.Model()), ExportPaths.EventsCsv));

    [Fact]
    public void Parquet_round_trips_through_read_parquet_with_native_types()
    {
        var rows = ReadParquet(
            "SELECT id, starts_utc, status, sequence, case_number, location_lat, location_lon, len(documents), "
            + "documents[1].url, documents[1].retrieved_utc, documents[1].sha256, documents[2].sha256 FROM parquet ORDER BY starts_utc");

        rows.Select(row => (string)row[0]!).ShouldBe([ExportSamples.October15.Id, ExportSamples.November3.Id, ExportSamples.November5.Id]);

        var october15 = rows[0];
        october15[1].ShouldBe(new DateTime(2026, 10, 15, 21, 30, 0));
        (october15[2], october15[3], october15[4]).ShouldBe(("cancelled", (object)1, "Z-26-01"));
        (october15[5], october15[6]).ShouldBe((null, null));
        october15[7].ShouldBe(2L);
        october15[8].ShouldBe(ExportSamples.NewsStory.Url.AbsoluteUri, "documents keep the record's order");
        october15[9].ShouldBe(ExportSamples.NewsStory.RetrievedAtUtc.UtcDateTime);
        (october15[10], october15[11]).ShouldBe((ExportSamples.NewsStory.Id.Value, ExportSamples.CountyAgenda1599.Id.Value));

        var november5 = rows[2];
        november5[1].ShouldBe(new DateTime(2026, 11, 5, 22, 30, 0), "17:30 EST is 22:30 UTC");
        (november5[5], november5[6]).ShouldBe((36.0726, -79.792));
        november5[11].ShouldBeNull();
    }

    [Fact]
    public void Parquet_is_the_same_bytes_for_the_same_input()
    {
        var first = Parquet();

        Parquet().ShouldBe(first);
        first.Take(4).ShouldBe("PAR1"u8.ToArray());
    }

    private static byte[] Parquet() =>
        TestFiles.Single(new TabularExporter().Export(ExportSamples.Model()), ExportPaths.EventsParquet).Content.ToArray();

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Test-only: constant SQL and a temporary path quoted by SqlLiteral.")]
    private static List<object?[]> ReadParquet(string select)
    {
        var directory = Directory.CreateTempSubdirectory("dcl-parquet-");
        try
        {
            var path = Path.Combine(directory.FullName, "events.parquet").Replace('\\', '/');
            File.WriteAllBytes(path, Parquet());
            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"WITH parquet AS (SELECT * FROM read_parquet({LedgerTables.SqlLiteral(path)})) {select}";
            using var reader = command.ExecuteReader();
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                rows.Add(row);
            }

            return rows;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

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
        october15[13].ShouldBe("https://news.example.org/2026/10/fixture-county-hearing https://fixture-county.example.org/agendas/1599.pdf");
        october15[14].ShouldBe("2026-10-11T09:30:00Z 2026-10-10T12:00:00Z");
        october15[15].ShouldBe($"{ExportSamples.NewsStory.Id.Value} {ExportSamples.CountyAgenda1599.Id.Value}");

        rows[2][4].ShouldBe(ExportSamples.November3.Title);
        rows[3][5].ShouldBe(string.Empty);
        (october15[11], october15[12]).ShouldBe((string.Empty, string.Empty));
        (rows[3][11], rows[3][12]).ShouldBe(("36.0726", "-79.792"));
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
