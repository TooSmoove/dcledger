using DcLedger.Application;
using DuckDB.NET.Data;

namespace DcLedger.Publishing.Tabular;

/// <summary>
/// Builds the DuckDB tables from the export model and writes <c>public/events.csv</c> (one row per event, RFC 4180
/// quoting, CRLF records, UTF-8) and <c>public/events.parquet</c> (the same rows, typed, with each event's documents as
/// a list; #30). The CSV safety rules (BOM, formula guard, data dictionary; R30, #31) build on this.
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
            new ExportFile(ExportPaths.EventsCsv, CopyTo(connection, LedgerTables.EventsCsvQuery, "export.csv", CsvOptions)),
            new ExportFile(ExportPaths.EventsParquet, CopyTo(connection, LedgerTables.EventsParquetQuery, "export.parquet", ParquetOptions)),
        ];
    }

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
