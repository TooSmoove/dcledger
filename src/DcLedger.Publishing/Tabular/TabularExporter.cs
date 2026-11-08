using DcLedger.Application;
using DuckDB.NET.Data;

namespace DcLedger.Publishing.Tabular;

/// <summary>
/// Builds the DuckDB tables from the export model and writes <c>events.csv</c> and <c>events.parquet</c> under
/// <c>public/</c>, and the same pair for local actions (<c>actions.csv</c>, <c>actions.parquet</c>; issue #90).
/// The CSV is for spreadsheets (SPEC AC7): UTF-8 with a BOM so Excel reads it as UTF-8, RFC 4180 quoting, CRLF
/// records, and the formula guard on every text cell (<see cref="LedgerTables.CsvTextMacro"/>). The Parquet file has
/// the same rows, typed, with each event's documents as a list (#30). Columns: docs/data-dictionary.md.
/// </summary>
public sealed class TabularExporter : IExporter
{
    /// <inheritdoc />
    public IReadOnlyList<ExportFile> Export(ExportModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        LedgerTables.Load(connection, model);
        return
        [
            new ExportFile(ExportPaths.EventsCsv, Csv(connection, LedgerTables.EventsCsvQuery)),
            new ExportFile(ExportPaths.EventsParquet, CopyTo(connection, LedgerTables.EventsParquetQuery, "export.parquet", ParquetOptions)),
            new ExportFile(ExportPaths.ActionsCsv, Csv(connection, LedgerTables.ActionsCsvQuery)),
            new ExportFile(ExportPaths.ActionsParquet, CopyTo(connection, LedgerTables.ActionsParquetQuery, "export.parquet", ParquetOptions)),
        ];
    }

    private static byte[] Csv(DuckDBConnection connection, string query) =>
        [.. Utf8Bom, .. CopyTo(connection, query, "export.csv", CsvOptions)];

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private const string CsvOptions = """FORMAT csv, HEADER true, DELIMITER ',', QUOTE '"', ESCAPE '"', NEW_LINE '\r\n'""";

    // Explicit, so a new DuckDB default can't change the bytes; one thread (LedgerTables) keeps one row group in order.
    private const string ParquetOptions = "FORMAT parquet, COMPRESSION snappy";

    /// <summary>Runs <c>COPY (query) TO</c> a temporary file and returns its bytes; DuckDB can only copy to files.</summary>
    private static byte[] CopyTo(DuckDBConnection connection, string query, string fileName, string options)
    {
        var directory = Directory.CreateTempSubdirectory("dcl-export-");
        try
        {
            var path = Path.Combine(directory.FullName, fileName).Replace('\\', '/');
            LedgerTables.Execute(connection, $"COPY ({query}) TO {LedgerTables.SqlLiteral(path)} ({options})");
            return File.ReadAllBytes(path);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
