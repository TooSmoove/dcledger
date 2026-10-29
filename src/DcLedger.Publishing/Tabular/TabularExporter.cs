using DcLedger.Application;
using DuckDB.NET.Data;

namespace DcLedger.Publishing.Tabular;

/// <summary>
/// Builds the DuckDB tables from the export model and writes <c>public/events.csv</c>: one row per event, RFC 4180
/// quoting, CRLF records, UTF-8. Parquet (R29, #30) and the CSV safety rules (BOM, formula guard, data dictionary;
/// R30, #31) build on this.
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
        return [new ExportFile(ExportPaths.EventsCsv, CopyToCsv(connection, LedgerTables.EventsCsvQuery))];
    }

    /// <summary>Runs <c>COPY (query) TO</c> a temporary file and returns its bytes; DuckDB can only copy to files.</summary>
    private static byte[] CopyToCsv(DuckDBConnection connection, string query)
    {
        var directory = Directory.CreateTempSubdirectory("dcl-export-");
        try
        {
            var path = Path.Combine(directory.FullName, "export.csv").Replace('\\', '/');
            LedgerTables.Execute(connection,
                $"""COPY ({query}) TO {LedgerTables.SqlLiteral(path)} (FORMAT csv, HEADER true, DELIMITER ',', QUOTE '"', ESCAPE '"', NEW_LINE '\r\n')""");
            return File.ReadAllBytes(path);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
