using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DcLedger.Application;
using DcLedger.Domain;
using DuckDB.NET.Data;

namespace DcLedger.Publishing.Tabular;

/// <summary>
/// Loads the export model into DuckDB tables (ADR-0002: DuckDB exists only to produce CSV and Parquet).
/// UTC instants are plain TIMESTAMPs, so nothing depends on ICU or the machine's time zone (spike S7). The connection
/// runs on one thread, keeps insertion order and never downloads an extension, so a query gives the same rows in the
/// same order on every run.
/// </summary>
internal static class LedgerTables
{
    /// <summary>
    /// One row per event, documents joined in the record's order, space-separated (URLs can't contain spaces). Every text
    /// column goes through <c>csv_text</c> (see <see cref="CsvTextMacro"/>); numbers stay numbers.
    /// </summary>
    public const string EventsCsvQuery = """
        SELECT
            csv_text(e.id) AS id,
            csv_text(e.uid) AS uid,
            csv_text(e.jurisdiction_id) AS jurisdiction_id,
            csv_text(e.body) AS body,
            csv_text(e.title) AS title,
            csv_text(e.case_number) AS case_number,
            csv_text(strftime(e.starts_utc, '%Y-%m-%dT%H:%M:%SZ')) AS starts_utc,
            csv_text(e.starts_local) AS starts_local,
            csv_text(e.status) AS status,
            e.sequence,
            csv_text(e.label) AS label,
            e.location_lat,
            e.location_lon,
            csv_text(string_agg(d.url, ' ' ORDER BY ed.position)) AS document_urls,
            csv_text(string_agg(strftime(d.retrieved_utc, '%Y-%m-%dT%H:%M:%SZ'), ' ' ORDER BY ed.position)) AS document_retrieved_utc,
            csv_text(string_agg(d.sha256, ' ' ORDER BY ed.position)) AS document_sha256
        FROM events AS e
        JOIN event_documents AS ed ON ed.event_id = e.id
        JOIN documents AS d ON d.sha256 = ed.sha256
        GROUP BY e.id, e.uid, e.jurisdiction_id, e.body, e.title, e.case_number, e.starts_utc, e.starts_local, e.status,
            e.sequence, e.label, e.location_lat, e.location_lon
        ORDER BY e.starts_utc, e.id
        """;

    /// <summary>
    /// The Parquet rows: the CSV's columns with native types (<c>starts_utc</c> a UTC timestamp, <c>sequence</c> an
    /// integer, coordinates doubles) and the documents as one ordered list of structs instead of three joined strings.
    /// </summary>
    public const string EventsParquetQuery = """
        SELECT
            e.id,
            e.uid,
            e.jurisdiction_id,
            e.body,
            e.title,
            e.case_number,
            e.starts_utc,
            e.starts_local,
            e.status,
            e.sequence,
            e.label,
            e.location_lat,
            e.location_lon,
            list({'url': d.url, 'retrieved_utc': d.retrieved_utc, 'sha256': d.sha256} ORDER BY ed.position) AS documents
        FROM events AS e
        JOIN event_documents AS ed ON ed.event_id = e.id
        JOIN documents AS d ON d.sha256 = ed.sha256
        GROUP BY e.id, e.uid, e.jurisdiction_id, e.body, e.title, e.case_number, e.starts_utc, e.starts_local, e.status,
            e.sequence, e.label, e.location_lat, e.location_lon
        ORDER BY e.starts_utc, e.id
        """;

    /// <summary>
    /// One row per local action (issue #90), in the order of <c>events.json</c>'s actions, documents joined like the
    /// events' and every text column guarded (see <see cref="CsvTextMacro"/>). <c>unanimous</c> is <c>true</c> or
    /// <c>false</c>; <c>sequence</c> a number.
    /// </summary>
    public const string ActionsCsvQuery = """
        SELECT
            csv_text(a.id) AS id,
            csv_text(a.jurisdiction_id) AS jurisdiction_id,
            csv_text(a.event_id) AS event_id,
            csv_text(a.action_type) AS action_type,
            csv_text(a.outcome) AS outcome,
            csv_text(a.decided_on) AS decided_on,
            csv_text(a.votes) AS votes,
            a.unanimous,
            a.sequence,
            csv_text(a.label) AS label,
            csv_text(string_agg(d.url, ' ' ORDER BY ad.position)) AS document_urls,
            csv_text(string_agg(strftime(d.retrieved_utc, '%Y-%m-%dT%H:%M:%SZ'), ' ' ORDER BY ad.position)) AS document_retrieved_utc,
            csv_text(string_agg(d.sha256, ' ' ORDER BY ad.position)) AS document_sha256
        FROM actions AS a
        JOIN action_documents AS ad ON ad.action_id = a.id
        JOIN documents AS d ON d.sha256 = ad.sha256
        GROUP BY a.id, a.jurisdiction_id, a.event_id, a.action_type, a.outcome, a.decided_on, a.votes, a.unanimous, a.sequence,
            a.label
        ORDER BY a.decided_on, a.id
        """;

    /// <summary>The actions' Parquet rows: native types (<c>decided_on</c> a DATE) and documents as a list of structs.</summary>
    public const string ActionsParquetQuery = """
        SELECT
            a.id,
            a.jurisdiction_id,
            a.event_id,
            a.action_type,
            a.outcome,
            CAST(a.decided_on AS DATE) AS decided_on,
            a.votes,
            a.unanimous,
            a.sequence,
            a.label,
            list({'url': d.url, 'retrieved_utc': d.retrieved_utc, 'sha256': d.sha256} ORDER BY ad.position) AS documents
        FROM actions AS a
        JOIN action_documents AS ad ON ad.action_id = a.id
        JOIN documents AS d ON d.sha256 = ad.sha256
        GROUP BY a.id, a.jurisdiction_id, a.event_id, a.action_type, a.outcome, a.decided_on, a.votes, a.unanimous, a.sequence,
            a.label
        ORDER BY a.decided_on, a.id
        """;

    private static readonly string[] Settings =
    [
        "SET autoinstall_known_extensions = false",
        "SET autoload_known_extensions = false",
        "SET threads = 1",
        "SET preserve_insertion_order = true",
        CsvTextMacro,
    ];

    /// <summary>
    /// The CSV formula guard (SPEC AC7, OWASP "CSV injection"): a text cell starting with <c>=</c>, <c>+</c>, <c>-</c>,
    /// <c>@</c>, tab or CR gets a leading apostrophe, so a spreadsheet shows it as text instead of running it. A cell
    /// already starting with an apostrophe gets one too, so the rule reverses exactly: drop one leading apostrophe from
    /// any text cell that has one (docs/data-dictionary.md). CSV only; Parquet keeps the values as they are.
    /// </summary>
    internal const string CsvTextMacro =
        """CREATE MACRO csv_text(value) AS CASE WHEN left(value, 1) IN ('=', '+', '-', '@', chr(9), chr(13), '''') THEN '''' || value ELSE value END""";

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
            label VARCHAR NOT NULL,
            location_lat DOUBLE,
            location_lon DOUBLE
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
        """
        CREATE TABLE actions (
            id VARCHAR PRIMARY KEY,
            jurisdiction_id VARCHAR NOT NULL,
            event_id VARCHAR,
            action_type VARCHAR NOT NULL,
            outcome VARCHAR NOT NULL,
            decided_on VARCHAR NOT NULL,
            votes VARCHAR,
            unanimous BOOLEAN NOT NULL,
            sequence INTEGER NOT NULL,
            label VARCHAR NOT NULL
        )
        """,
        """
        CREATE TABLE action_documents (
            action_id VARCHAR NOT NULL,
            position INTEGER NOT NULL,
            sha256 VARCHAR NOT NULL,
            PRIMARY KEY (action_id, position)
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
        AppendDocuments(connection, model.Events.SelectMany(item => item.Documents).Concat(model.Actions.SelectMany(item => item.Documents)));
        AppendEvents(connection, model.Events);
        AppendActions(connection, model.Actions);
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

    private static void AppendDocuments(DuckDBConnection connection, IEnumerable<Document> all)
    {
        var documents = all
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
                    .AppendValue(ledgerEvent.Location?.Latitude)
                    .AppendValue(ledgerEvent.Location?.Longitude)
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

    private static void AppendActions(DuckDBConnection connection, IReadOnlyList<ExportAction> actions)
    {
        using (var appender = connection.CreateAppender("actions"))
        {
            foreach (var item in actions)
            {
                var action = item.Action;
                appender.CreateRow()
                    .AppendValue(action.Id)
                    .AppendValue(action.JurisdictionId)
                    .AppendValue(action.EventId)
                    .AppendValue(action.ActionType.ToToken())
                    .AppendValue(action.Outcome.ToToken())
                    .AppendValue(action.DecidedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    .AppendValue(action.Votes)
                    .AppendValue(action.Unanimous)
                    .AppendValue(action.Sequence)
                    .AppendValue(action.Label.ToToken())
                    .EndRow();
            }
        }

        using var links = connection.CreateAppender("action_documents");
        foreach (var item in actions)
        {
            for (var position = 0; position < item.Documents.Count; position++)
            {
                links.CreateRow()
                    .AppendValue(item.Action.Id)
                    .AppendValue(position)
                    .AppendValue(item.Documents[position].Id.Value)
                    .EndRow();
            }
        }
    }
}
