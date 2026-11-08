using System.Diagnostics.CodeAnalysis;
using DcLedger.Application;
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

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly string[] ActionsHeader =
    [
        "id", "jurisdiction_id", "event_id", "action_type", "outcome", "decided_on", "votes", "unanimous", "sequence", "label",
        "document_urls", "document_retrieved_utc", "document_sha256",
    ];

    /// <summary>The CSV as text, without its BOM (asserted on the bytes below).</summary>
    private static string EventsCsv(ExportModel? model = null) =>
        TestFiles.Text(EventsCsvFile(model)).TrimStart('﻿');

    private static ExportFile EventsCsvFile(ExportModel? model = null) =>
        TestFiles.Single(new TabularExporter().Export(model ?? ExportSamples.Model()), ExportPaths.EventsCsv);

    /// <summary>Reverses the formula guard as docs/data-dictionary.md says: drop one leading apostrophe.</summary>
    private static string Unguard(string cell) => cell.StartsWith('\'') ? cell[1..] : cell;

    [Fact]
    public void Csv_starts_with_a_utf8_bom_and_records_end_in_crlf()
    {
        var bytes = EventsCsvFile().Content.ToArray();
        var text = EventsCsv();

        bytes.Take(3).ShouldBe(Utf8Bom);
        bytes.Skip(3).Take(3).ShouldBe("id,"u8.ToArray(), "the header follows the BOM directly");
        text.EndsWith("\r\n", StringComparison.Ordinal).ShouldBeTrue();
        text.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\n', StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Every_formula_trigger_is_guarded_in_text_cells_and_round_trips()
    {
        // The six triggers from SPEC AC7, plus a leading apostrophe so the guard reverses exactly; one per text field.
        var sample = ExportSamples.Model();
        var hostile = new Dictionary<string, (string Body, string Title, string? CaseNumber)>(StringComparer.Ordinal)
        {
            [ExportSamples.October15.Id] = ("=SUM(1,2)", "+cmd|' /C calc'!A0", "-2+3"),
            [ExportSamples.November3.Id] = ("@SUM(A1)", "\tTabbed title", "\rCR case"),
            [ExportSamples.November5.Id] = ("Board of Commissioners", "'Quoted title", "a=b, not a formula"),
        };
        var model = sample with
        {
            Events = [.. sample.Events.Select(item =>
            {
                var (body, title, caseNumber) = hostile[item.Event.Id];
                return item with { Event = item.Event with { Body = body, Title = title, CaseNumber = caseNumber } };
            })],
        };

        var rows = Rfc4180.Parse(EventsCsv(model));

        rows.Count.ShouldBe(4);
        foreach (var row in rows.Skip(1))
        {
            var (body, title, caseNumber) = hostile[row[0]];
            (Unguard(row[3]), Unguard(row[4]), Unguard(row[5])).ShouldBe((body, title, caseNumber ?? string.Empty), row[0]);
        }

        var october15 = rows[1];
        (october15[3], october15[4], october15[5]).ShouldBe(("'=SUM(1,2)", "'+cmd|' /C calc'!A0", "'-2+3"));
        var november3 = rows[2];
        (november3[3], november3[4], november3[5]).ShouldBe(("'@SUM(A1)", "'\tTabbed title", "'\rCR case"));
        var november5 = rows[3];
        (november5[3], november5[4], november5[5]).ShouldBe(("Board of Commissioners", "''Quoted title", "a=b, not a formula"));
        november5[12].ShouldBe("-79.792", "numbers aren't text, so a negative longitude stays a number");
    }

    [Fact]
    public void Parquet_keeps_values_unguarded()
    {
        var sample = ExportSamples.Model();
        var model = sample with
        {
            Events = [.. sample.Events.Select(item => item with { Event = item.Event with { Title = "=formula" } })],
        };
        var directory = Directory.CreateTempSubdirectory("dcl-parquet-");
        try
        {
            var path = Path.Combine(directory.FullName, "events.parquet");
            File.WriteAllBytes(path, TestFiles.Single(new TabularExporter().Export(model), ExportPaths.EventsParquet).Content.ToArray());

            ReadParquet("SELECT DISTINCT title FROM parquet", path).ShouldHaveSingleItem()[0].ShouldBe("=formula");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

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

    /// <summary>Runs <paramref name="select"/> against the sample ledger's Parquet file, exposed as <c>parquet</c>.</summary>
    private static List<object?[]> ReadParquet(string select)
    {
        var directory = Directory.CreateTempSubdirectory("dcl-parquet-");
        try
        {
            var path = Path.Combine(directory.FullName, "events.parquet");
            File.WriteAllBytes(path, Parquet());
            return ReadParquet(select, path);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>Runs <paramref name="select"/> against the Parquet file at <paramref name="path"/>, exposed as <c>parquet</c>.</summary>
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Test-only: constant SQL and a temporary path quoted by SqlLiteral.")]
    private static List<object?[]> ReadParquet(string select, string path)
    {
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"WITH parquet AS (SELECT * FROM read_parquet({LedgerTables.SqlLiteral(path.Replace('\\', '/'))})) {select}";
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

    // Verify compares text with LF line endings and without the BOM; both are asserted on the bytes above.
    [Fact]
    public Task Events_csv_golden() => Verify(EventsCsv().ReplaceLineEndings("\n"));

    [Fact]
    public Task Actions_csv_golden() => Verify(ActionsCsv().ReplaceLineEndings("\n"));

    [Fact]
    public void Actions_csv_has_the_bom_crlf_and_guard_of_the_events_csv()
    {
        var sample = ExportSamples.Model();
        var hostile = sample with
        {
            Actions = [.. sample.Actions.Select(item => item with { Action = item.Action with { Votes = null, Unanimous = true, Sequence = 3 } })],
        };
        var bytes = TestFiles.Single(new TabularExporter().Export(hostile), ExportPaths.ActionsCsv).Content.ToArray();
        var rows = Rfc4180.Parse(TestFiles.Text(TestFiles.Single(new TabularExporter().Export(hostile), ExportPaths.ActionsCsv)).TrimStart('\uFEFF'));

        bytes.Take(3).ShouldBe(Utf8Bom);
        rows[0].ShouldBe(ActionsHeader);
        var row = rows.Skip(1).ShouldHaveSingleItem();
        (row[6], row[7], row[8]).ShouldBe((string.Empty, "true", "3"), "votes empty, unanimous and sequence as written");
    }

    [Fact]
    public void Actions_parquet_round_trips_with_native_types()
    {
        var directory = Directory.CreateTempSubdirectory("dcl-parquet-");
        try
        {
            var path = Path.Combine(directory.FullName, "actions.parquet");
            File.WriteAllBytes(path, TestFiles.Single(new TabularExporter().Export(ExportSamples.Model()), ExportPaths.ActionsParquet).Content.ToArray());

            var row = ReadParquet("SELECT id, decided_on, votes, unanimous, sequence, len(documents), documents[1].sha256 FROM parquet", path)
                .ShouldHaveSingleItem();

            row[0].ShouldBe(ExportSamples.November5Moratorium.Id);
            var decidedOn = row[1] switch
            {
                DateOnly date => date,
                DateTime dateTime => DateOnly.FromDateTime(dateTime),
                var other => throw new InvalidOperationException($"decided_on read back as {other?.GetType().Name ?? "null"}."),
            };
            decidedOn.ShouldBe(new DateOnly(2026, 11, 5), "a DATE, not text");
            (row[2], row[3], row[4], row[5]).ShouldBe(("8-1", (object)false, (object)0, (object)1L));
            row[6].ShouldBe(ExportSamples.CountyActionDetail1600.Id.Value, "a document only the action cites is still in the documents table");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void No_actions_gives_just_the_actions_header()
    {
        var model = ExportSamples.Model() with { Actions = [] };

        var csv = TestFiles.Text(TestFiles.Single(new TabularExporter().Export(model), ExportPaths.ActionsCsv)).TrimStart('\uFEFF');

        Rfc4180.Parse(csv).ShouldHaveSingleItem().ShouldBe(ActionsHeader);
    }

    private static string ActionsCsv() =>
        TestFiles.Text(TestFiles.Single(new TabularExporter().Export(ExportSamples.Model()), ExportPaths.ActionsCsv)).TrimStart('\uFEFF');

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

        var csv = EventsCsv(model);

        Rfc4180.Parse(csv).ShouldHaveSingleItem().ShouldBe(Header);
    }
}
