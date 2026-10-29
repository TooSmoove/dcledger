using System.Diagnostics.CodeAnalysis;
using DcLedger.Application;
using DcLedger.Domain;
using DuckDB.NET.Data;

namespace DcLedger.Publishing.Tabular;

/// <summary>
/// Loads the export model into DuckDB tables (ADR-0002: DuckDB exists only to produce CSV, and Parquet in R29).
/// UTC instants are plain TIMESTAMPs, so nothing depends on ICU or the machine's time zone (spike S7). The connection
/// runs on one thread, keeps insertion order and never downloads an extension, so a query gives the same rows in the
/// same order on every run.
/// </summary>
internal static class LedgerTables
{
    /// <summary>One row per event, documents joined in the record's order, space-separated (URLs can't contain spaces).</summary>
    public const string EventsCsvQuery = """
        SELECT
            e.id,
            e.uid,
            e.jurisdiction_id,
            e.body,
            e.title,
            e.case_number,
            strftime(e.starts_utc, '%Y-%m-%dT%H:%M:%SZ') AS starts_utc,
            e.starts_local,
            e.status,
            e.sequence,
            e.label,
            string_agg(d.url, ' ' ORDER BY ed.position) AS document_urls,
            string_agg(strftime(d.retrieved_utc, '%Y-%m-%dT%H:%M:%SZ'), ' ' ORDER BY ed.position) AS document_retrieved_utc,
            string_agg(d.sha256, ' ' ORDER BY ed.position) AS document_sha256
        FROM events AS e
        JOIN event_documents AS ed ON ed.event_id = e.id
        JOIN documents AS d ON d.sha256 = ed.sha256
        GROUP BY e.id, e.uid, e.jurisdiction_id, e.body, e.title, e.case_number, e.starts_utc, e.starts_local, e.status,
            e.sequence, e.label
        ORDER BY e.starts_utc, e.id
        """;

    private static readonly string[] Settings =
    [
        "SET autoinstall_known_extensions = false",
        "SET autoload_known_extensions = false",
        "SET threads = 1",
        "SET preserve_insertion_order = true",
    ];

    private static readonly string[] Schema =
    [
        """
        CREATE TABLE jurisdictions (
            id VARCHAR PRIMARY KEY,
            name VARCHAR NOT NULL,
            platform VARCHAR NOT NULL,
            last_checked_utc TIMESTAMP,
            stale BOOLEAN NOT NULL
        )
        """,
        """
        CREATE TABLE documents (
            sha256 VARCHAR PRIMARY KEY,
            url VARCHAR NOT NULL,
            retrieved_utc TIMESTAMP NOT NULL,
            media_type VARCHAR NOT NULL,
            bytes BIGINT NOT NULL,
            source_type VARCHAR NOT NULL
        )
        """,
        """
        CREATE TABLE events (
            id VARCHAR PRIMARY KEY,
            uid VARCHAR NOT NULL,
            jurisdiction_id VARCHAR NOT NULL,
            platform VARCHAR NOT NULL,
            platform_meeting_id VARCHAR NOT NULL,
            body VARCHAR NOT NULL,
            title VARCHAR NOT NULL,
            case_number VARCHAR,
            starts_utc TIMESTAMP NOT NULL,
            starts_local VARCHAR NOT NULL,
            status VARCHAR NOT NULL,
            sequence INTEGER NOT NULL,
            label VARCHAR NOT NULL
        )
        """,
        """
        CREATE TABLE event_documents (
            event_id VARCHAR NOT NULL,
            position INTEGER NOT NULL,
            sha256 VARCHAR NOT NULL,
            PRIMARY KEY (event_id, position)
        )
        """,
    ];

    /// <summary>Configures <paramref name="connection"/> (already open) and loads <paramref name="model"/> into it.</summary>
    public static void Load(DuckDBConnection connection, ExportModel model)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(model);

        foreach (var statement in Settings.Concat(Schema))
        {
            Execute(connection, statement);
        }

        AppendJurisdictions(connection, model.Jurisdictions);
        AppendDocuments(connection, model.Events);
        AppendEvents(connection, model.Events);
    }

    /// <summary>Runs one statement built from this class's constants and paths it created.</summary>
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Statements are constants here, or COPY with a temporary path quoted by SqlLiteral; no data reaches SQL text.")]
    public static void Execute(DuckDBConnection connection, string sql)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Quotes <paramref name="value"/> as a SQL string literal.</summary>
    public static string SqlLiteral(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static void AppendJurisdictions(DuckDBConnection connection, IReadOnlyList<ExportJurisdiction> jurisdictions)
    {
        using var appender = connection.CreateAppender("jurisdictions");
        foreach (var item in jurisdictions)
        {
            appender.CreateRow()
                .AppendValue(item.Jurisdiction.Id)
                .AppendValue(item.Jurisdiction.Name)
                .AppendValue(item.Jurisdiction.Platform.ToToken())
                .AppendValue(item.LastCheckedUtc?.UtcDateTime)
                .AppendValue(item.Stale)
                .EndRow();
        }
    }

    private static void AppendDocuments(DuckDBConnection connection, IReadOnlyList<ExportEvent> events)
    {
        var documents = events
            .SelectMany(item => item.Documents)
            .DistinctBy(document => document.Id)
            .OrderBy(document => document.Id.Value, StringComparer.Ordinal);

        using var appender = connection.CreateAppender("documents");
        foreach (var document in documents)
        {
            appender.CreateRow()
                .AppendValue(document.Id.Value)
                .AppendValue(document.Url.AbsoluteUri)
                .AppendValue(document.RetrievedAtUtc.UtcDateTime)
                .AppendValue(document.MediaType)
                .AppendValue(document.Bytes)
                .AppendValue(document.SourceType.ToToken())
                .EndRow();
        }
    }

    private static void AppendEvents(DuckDBConnection connection, IReadOnlyList<ExportEvent> events)
    {
        using (var appender = connection.CreateAppender("events"))
        {
            foreach (var item in events)
            {
                var ledgerEvent = item.Event;
                appender.CreateRow()
                    .AppendValue(ledgerEvent.Id)
                    .AppendValue(ledgerEvent.Uid)
                    .AppendValue(ledgerEvent.JurisdictionId)
                    .AppendValue(ledgerEvent.Platform.ToToken())
                    .AppendValue(ledgerEvent.PlatformMeetingId)
                    .AppendValue(ledgerEvent.Body)
                    .AppendValue(ledgerEvent.Title)
                    .AppendValue(ledgerEvent.CaseNumber)
                    .AppendValue(ledgerEvent.StartsAt.UtcDateTime)
                    .AppendValue(IsoTime.Eastern(ledgerEvent.StartsAt))
                    .AppendValue(ledgerEvent.Status.ToToken())
                    .AppendValue(ledgerEvent.Sequence)
                    .AppendValue(ledgerEvent.Label.ToToken())
                    .EndRow();
            }
        }

        using var links = connection.CreateAppender("event_documents");
        foreach (var item in events)
        {
            for (var position = 0; position < item.Documents.Count; position++)
            {
                links.CreateRow()
                    .AppendValue(item.Event.Id)
                    .AppendValue(position)
                    .AppendValue(item.Documents[position].Id.Value)
                    .EndRow();
            }
        }
    }
}
