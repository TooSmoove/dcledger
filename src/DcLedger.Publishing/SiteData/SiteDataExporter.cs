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
/// Adding a key the site can ignore, such as <c>actions</c> (issue #25) or a jurisdiction's <c>coverage_tier</c> and a
/// decision's terms (issue #115), isn't breaking.
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
        json.WriteNumber("stale_after_hours", (int)item.StaleAfter.TotalHours);
        json.WriteString("coverage_tier", item.Jurisdiction.CoverageTier.ToToken());
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
        json.WriteNumber("sequence", action.Sequence);
        json.WriteString("label", action.Label.ToToken());
        WriteDocuments(json, item.Documents);
        WriteTerms(json, action.Terms);
        json.WriteEndObject();
    }

    /// <summary>
    /// A decision's terms and status (issue #115), added within schema version 1 after <c>documents</c>: each key is
    /// always written, null when the record doesn't state it. Dates are <c>YYYY-MM-DD</c>.
    /// </summary>
    private static void WriteTerms(Utf8JsonWriter json, DecisionTerms terms)
    {
        WriteStringOrNull(json, "instrument_number", terms.InstrumentNumber);
        WriteStringOrNull(json, "threshold", terms.Threshold);
        WriteStringOrNull(json, "scope", terms.Scope);
        WriteStringOrNull(json, "effective_on", IsoTime.Date(terms.EffectiveOn));
        WriteStringOrNull(json, "expires_on", IsoTime.Date(terms.ExpiresOn));
        WriteStringOrNull(json, "early_termination", terms.EarlyTermination);
        WriteStringOrNull(json, "exemptions", terms.Exemptions);
        WriteStringOrNull(json, "successor_id", terms.SuccessorId);
        WriteStringOrNull(json, "status", terms.Status?.ToToken());
    }

    /// <summary>
    /// Provenance for one item (SPEC AC2): URL, retrieval time and SHA-256 of each document, and how it was obtained
    /// (<c>fetched</c> or <c>manual</c>; added within schema version 1, issue #113). Then, also within schema version 1
    /// (issue #132), the document's <c>kind</c> and its first-page <c>thumbnail</c> (path, width and height), each null
    /// when the record has none.
    /// </summary>
    private static void WriteDocuments(Utf8JsonWriter json, IReadOnlyList<Document> documents)
    {
        json.WriteStartArray("documents");
        foreach (var document in documents)
        {
            json.WriteStartObject();
            json.WriteString("url", document.Url.AbsoluteUri);
            json.WriteString("retrieved_utc", IsoTime.Utc(document.RetrievedAtUtc));
            json.WriteString("sha256", document.Id.Value);
            json.WriteString("retrieval", document.Retrieval.ToToken());
            WriteStringOrNull(json, "kind", document.Kind?.ToToken());
            WriteThumbnail(json, document);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary><c>"thumbnail": {"path": "/thumbnails/{sha256}.webp", "width": 240, "height": 311}</c>, or null.</summary>
    private static void WriteThumbnail(Utf8JsonWriter json, Document document)
    {
        if (document.Thumbnail is not { } thumbnail)
        {
            json.WriteNull("thumbnail");
            return;
        }

        json.WriteStartObject("thumbnail");
        json.WriteString("path", ExportPaths.ThumbnailUrlPath(document.Id.Value));
        json.WriteNumber("width", thumbnail.Width);
        json.WriteNumber("height", thumbnail.Height);
        json.WriteEndObject();
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
