using DuckDB.NET.Data;
using Shouldly;
using Xunit;

namespace Spikes.DuckDb;

/// <summary>Spike S7: bundled natives + Parquet export with no extension downloads.</summary>
public class DuckDbSpikeTests
{
    [Fact]
    public void Native_library_for_this_os_is_in_the_output()
    {
        var runtimes = Path.Combine(AppContext.BaseDirectory, "runtimes");
        var rids = Directory.Exists(runtimes)
            ? Directory.GetDirectories(runtimes).Select(Path.GetFileName).Order().ToArray()
            : [];
        Findings.Write("S7", $"runtimes/ = [{string.Join(", ", rids)}]");

        var expected = OperatingSystem.IsWindows()
            ? Path.Combine(runtimes, "win-x64", "native", "duckdb.dll")
            : Path.Combine(runtimes, "linux-x64", "native", "libduckdb.so");
        File.Exists(expected).ShouldBeTrue($"expected {expected}");
    }

    [Fact]
    public void Parquet_copy_works_with_autoinstall_and_autoload_off()
    {
        var extensionDir = Directory.CreateTempSubdirectory("dcl-duckext").FullName.Replace('\\', '/');
        var outFile = Path.Combine(Directory.CreateTempSubdirectory("dcl-duckout").FullName, "event.parquet").Replace('\\', '/');

        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        Exec(connection, "SET autoinstall_known_extensions = false");
        Exec(connection, "SET autoload_known_extensions = false");
        Exec(connection, $"SET extension_directory = '{extensionDir}'");

        Findings.Write("S7", $"version={Scalar(connection, "SELECT version()")}");
        Findings.Write("S7", "extensions: " + Scalar(connection,
            "SELECT string_agg(extension_name || '=' || install_mode, ', ' ORDER BY extension_name) " +
            "FROM duckdb_extensions() WHERE extension_name IN ('core_functions','icu','json','parquet')"));

        // UTC instants stored as plain TIMESTAMP (no TIMESTAMPTZ) so nothing depends on ICU.
        Exec(connection, "CREATE TABLE event AS SELECT 'guilford-boc-2026-11-05' AS id, TIMESTAMP '2026-11-05 22:30:00' AS starts_utc");
        Exec(connection, $"COPY event TO '{outFile}' (FORMAT parquet)");

        Convert.ToInt64(Scalar(connection, $"SELECT count(*) FROM read_parquet('{outFile}')")).ShouldBe(1L);
        Directory.EnumerateFileSystemEntries(extensionDir).ShouldBeEmpty("no extension may be downloaded at runtime");
        Findings.Write("S7", "COPY TO parquet + read_parquet OK; extension dir empty");
    }

    private static void Exec(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
