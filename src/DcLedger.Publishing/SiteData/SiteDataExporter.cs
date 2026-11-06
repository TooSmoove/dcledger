using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Publishing.SiteData;

/// <summary>
/// Writes <c>site-data/events.json</c>, the site's only input (ADR-0001, ADR-0004). Field names and order are spelled
/// out here and mirrored by <c>site/src/content.config.ts</c>; a breaking change bumps <see cref="SchemaVersion"/>.
/// Adding a key the site can ignore, such as <c>actions</c> (issue #25), isn't breaking.
/// The output is LF-terminated UTF-8 without a BOM, the same on every operating system.
/// </summary>
public sealed class SiteDataExporter : IExporter
{
    /// <summary>The events.json schema this exporter writes; the site rejects any other.</summary>
    public const int SchemaVersion = 1;

    // Relaxed escaping keeps quotes and non-ASCII text readable; the site parses this as data, never as HTML.
    private static readonly JsonWriterOptions Options = new() { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <inheritdoc />
    public IReadOnlyList<ExportFile> Export(ExportModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, Options))
        {
            json.WriteStartObject();
            json.WriteNumber("schema_version", SchemaVersion);
            json.WriteString("generated_utc", IsoTime.Utc(model.GeneratedUtc));

            json.WriteStartArray("jurisdictions");
            foreach (var jurisdiction in model.Jurisdictions)
            {
                WriteJurisdiction(json, jurisdiction);
            }

            json.WriteEndArray();

            json.WriteStartArray("events");
            foreach (var exportEvent in model.Events)
            {
                WriteEvent(json, exportEvent);
            }

            json.WriteEndArray();

            json.WriteStartArray("actions");
            foreach (var exportAction in model.Actions)
            {
                WriteAction(json, exportAction);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        buffer.Write("\n"u8);
        return [new ExportFile(ExportPaths.SiteData, buffer.WrittenMemory)];
    }

    private static void WriteJurisdiction(Utf8JsonWriter json, ExportJurisdiction item)
    {
        json.WriteStartObject();
        json.WriteString("id", item.Jurisdiction.Id);
        json.WriteString("name", item.Jurisdiction.Name);
        WriteStringOrNull(json, "last_checked_utc", item.LastCheckedUtc is { } lastChecked ? IsoTime.Utc(lastChecked) : null);
        json.WriteBoolean("stale", item.Stale);
        json.WriteEndObject();
    }

    private static void WriteEvent(Utf8JsonWriter json, ExportEvent item)
    {
        var ledgerEvent = item.Event;
        json.WriteStartObject();
        json.WriteString("id", ledgerEvent.Id);
        json.WriteString("uid", ledgerEvent.Uid);
        json.WriteString("jurisdiction_id", ledgerEvent.JurisdictionId);
        json.WriteString("body", ledgerEvent.Body);
        json.WriteString("title", ledgerEvent.Title);
        WriteStringOrNull(json, "case_number", ledgerEvent.CaseNumber);
        json.WriteString("starts_utc", IsoTime.Utc(ledgerEvent.StartsAt));
        json.WriteString("starts_local", IsoTime.Eastern(ledgerEvent.StartsAt));
        json.WriteString("status", ledgerEvent.Status.ToToken());
        json.WriteNumber("sequence", ledgerEvent.Sequence);
        json.WriteString("label", ledgerEvent.Label.ToToken());
        WriteLocation(json, ledgerEvent.Location);
        WriteDocuments(json, item.Documents);
        json.WriteEndObject();
    }

    private static void WriteAction(Utf8JsonWriter json, ExportAction item)
    {
        var action = item.Action;
        json.WriteStartObject();
        json.WriteString("id", action.Id);
        json.WriteString("jurisdiction_id", action.JurisdictionId);
        WriteStringOrNull(json, "event_id", action.EventId);
        json.WriteString("action_type", action.ActionType.ToToken());
        json.WriteString("outcome", action.Outcome.ToToken());
        json.WriteString("decided_on", action.DecidedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        WriteStringOrNull(json, "votes", action.Votes);
        json.WriteBoolean("unanimous", action.Unanimous);
        json.WriteString("label", action.Label.ToToken());
        WriteDocuments(json, item.Documents);
        json.WriteEndObject();
    }

    /// <summary>Provenance for one item (SPEC AC2): URL, retrieval time and SHA-256 of each document.</summary>
    private static void WriteDocuments(Utf8JsonWriter json, IReadOnlyList<Document> documents)
    {
        json.WriteStartArray("documents");
        foreach (var document in documents)
        {
            json.WriteStartObject();
            json.WriteString("url", document.Url.AbsoluteUri);
            json.WriteString("retrieved_utc", IsoTime.Utc(document.RetrievedAtUtc));
            json.WriteString("sha256", document.Id.Value);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary><c>"location": {"lat": 36.0726, "lon": -79.792}</c>, or null. Numbers use the shortest exact form.</summary>
    private static void WriteLocation(Utf8JsonWriter json, Location? location)
    {
        if (location is null)
        {
            json.WriteNull("location");
            return;
        }

        json.WriteStartObject("location");
        json.WriteNumber("lat", location.Latitude);
        json.WriteNumber("lon", location.Longitude);
        json.WriteEndObject();
    }

    private static void WriteStringOrNull(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null)
        {
            json.WriteNull(name);
        }
        else
        {
            json.WriteString(name, value);
        }
    }
}
