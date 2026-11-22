using System.Diagnostics.CodeAnalysis;
using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Publishing.Feed;
using DcLedger.Publishing.Ics;
using DcLedger.Publishing.SiteData;
using DcLedger.Publishing.Tabular;
using DcLedger.Testing;
using DuckDB.NET.Data;
using Shouldly;
using Xunit;

namespace DcLedger.Publishing.Tests;

/// <summary>
/// The public data contract (issue #116, <c>docs/export.md</c> "Stability"): the paths subscribers use, the columns
/// the data dictionary documents and the vocabularies it lists. Each test fails when the code and the promise drift;
/// a deliberate breaking change updates the test, the dictionary and <c>CHANGELOG.md</c> together.
/// </summary>
public sealed class PublicContractTests
{
    /// <summary>
    /// Every path published at v0.1.0, and each added since (ADR-0010's calendars), spelled out rather than taken from <see cref="ExportPaths"/>, so renaming a
    /// constant fails here. The sample ledger has two watched jurisdictions, so two per-jurisdiction calendars.
    /// </summary>
    private static readonly string[] FrozenPaths =
    [
        "site-data/events.json",
        "public/radar.ics",
        "public/radar/fixture-city.ics",
        "public/radar/fixture-county.ics",
        "public/feed.xml",
        "public/events.csv",
        "public/events.parquet",
        "public/actions.csv",
        "public/actions.parquet",
        "public/ahead.ics", // ADR-0010
        "public/ahead/fixture-city.ics",
        "public/ahead/fixture-county.ics",
    ];

    private static readonly string[] DocumentColumns = ["document_urls", "document_retrieved_utc", "document_sha256", "document_retrieval"];

    [Fact]
    public void No_published_path_disappears_or_is_renamed()
    {
        var written = Exporters().SelectMany(exporter => exporter.Export(ExportSamples.Model())).Select(file => file.Path).ToList();

        FrozenPaths.Where(path => !written.Contains(path)).ShouldBeEmpty("published paths are frozen (docs/export.md, Stability); new paths may be added");
    }

    [Theory]
    [InlineData("## `events.csv`", ExportPaths.EventsCsv)]
    [InlineData("## `actions.csv` and `actions.parquet`", ExportPaths.ActionsCsv)]
    public void The_data_dictionary_lists_every_csv_column_in_order(string heading, string csvPath)
    {
        var header = CsvHeader(csvPath);

        // docs/data-dictionary.md and the exporter's header agree in name and order.
        DictionaryColumns(heading).ShouldBe(header);
    }

    [Theory]
    [InlineData("## `events.csv`", ExportPaths.EventsParquet)]
    [InlineData("## `actions.csv` and `actions.parquet`", ExportPaths.ActionsParquet)]
    public void Parquet_columns_are_the_documented_csv_columns_with_documents_as_one_list(string heading, string parquetPath)
    {
        var expected = WithDocumentsCollapsed(DictionaryColumns(heading));

        ParquetColumns(parquetPath).ShouldBe(expected);
    }

    [Fact]
    public void The_events_parquet_table_names_every_parquet_column()
    {
        var documented = MarkdownTables.After(Dictionary(), "## `events.parquet`").SelectMany(row => MarkdownTables.Names(row[0]));

        documented.Order(StringComparer.Ordinal).ShouldBe(ParquetColumns(ExportPaths.EventsParquet).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_dictionary_lists_every_vocabulary_value_the_code_writes()
    {
        var rows = MarkdownTables.After(Dictionary(), "## Vocabularies");

        Values(rows, "status", "events.csv").ShouldBe(Tokens<EventStatus>(EnumTokens.ToToken));
        Values(rows, "label").ShouldBe(Tokens<EvidenceLabel>(EnumTokens.ToToken));
        Values(rows, "document_retrieval").ShouldBe(Tokens<Retrieval>(EnumTokens.ToToken));
        Values(rows, "action_type").ShouldBe(Tokens<ActionType>(EnumTokens.ToToken));
        Values(rows, "outcome").ShouldBe(Tokens<ActionOutcome>(EnumTokens.ToToken));
        Values(rows, "status", "actions.csv").ShouldBe(Tokens<ActionStatus>(EnumTokens.ToToken));
        Values(rows, "coverage_tier").ShouldBe(Tokens<CoverageTier>(EnumTokens.ToToken));
        rows.Count.ShouldBe(7, "a new vocabulary gets a row here and a line above");
    }

    [Fact]
    public void A_renamed_column_fails_the_dictionary_check()
    {
        // Proves the drift check can fail: the documented events.csv columns with one renamed don't match the header.
        var renamed = DictionaryColumns("## `events.csv`").Select(name => name == "case_number" ? "case_no" : name).ToList();

        renamed.SequenceEqual(CsvHeader(ExportPaths.EventsCsv)).ShouldBeFalse();
    }

    private static IExporter[] Exporters() =>
        [new SiteDataExporter(), new RadarCalendarExporter(), new AheadCalendarExporter(new Uri("https://ncdcledger.org")), new FeedExporter(new Uri("https://ncdcledger.org")), new TabularExporter()];

    private static string Dictionary() => File.ReadAllText(TestFiles.RepositoryPath("docs", "data-dictionary.md"));

    /// <summary>The column names in the first cell of each row of the table under <paramref name="heading"/>, in order.</summary>
    private static List<string> DictionaryColumns(string heading) =>
        [.. MarkdownTables.After(Dictionary(), heading).SelectMany(row => MarkdownTables.Names(row[0]))];

    private static List<string> CsvHeader(string path)
    {
        var csv = TestFiles.Text(TestFiles.Single(new TabularExporter().Export(ExportSamples.Model()), path)).TrimStart('﻿');
        return [.. Rfc4180.Parse(csv)[0]];
    }

    /// <summary>The CSV columns with the four <c>document_*</c> columns replaced by the one <c>documents</c> list, as in Parquet.</summary>
    private static List<string> WithDocumentsCollapsed(List<string> csvColumns)
    {
        var first = csvColumns.IndexOf(DocumentColumns[0]);
        csvColumns.GetRange(first, DocumentColumns.Length).ShouldBe(DocumentColumns); // the four are adjacent
        return [.. csvColumns.Take(first), "documents", .. csvColumns.Skip(first + DocumentColumns.Length)];
    }

    /// <summary>The values listed for the vocabulary whose column cell starts with <paramref name="column"/> (and whose file cell mentions <paramref name="file"/>, when given).</summary>
    private static IReadOnlyList<string> Values(IReadOnlyList<string[]> rows, string column, string? file = null)
    {
        var row = rows.Single(r => FirstName(r[0]) == column && (file is null || r[1].Contains(file, StringComparison.Ordinal)));
        return MarkdownTables.Names(row[2]);
    }

    private static string? FirstName(string cell)
    {
        var names = MarkdownTables.Names(cell);
        return names.Count > 0 ? names[0] : null;
    }

    private static List<string> Tokens<TEnum>(Func<TEnum, string> token)
        where TEnum : struct, Enum => [.. Enum.GetValues<TEnum>().Select(token)];

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Test-only: constant SQL and a temporary path quoted by SqlLiteral.")]
    private static List<string> ParquetColumns(string path)
    {
        var directory = Directory.CreateTempSubdirectory("dcl-contract-");
        try
        {
            var file = Path.Combine(directory.FullName, "export.parquet");
            File.WriteAllBytes(file, TestFiles.Single(new TabularExporter().Export(ExportSamples.Model()), path).Content.ToArray());
            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT column_name FROM (DESCRIBE SELECT * FROM read_parquet({LedgerTables.SqlLiteral(file.Replace('\\', '/'))}))";
            using var reader = command.ExecuteReader();
            var columns = new List<string>();
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
            }

            return columns;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
