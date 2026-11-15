using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Publishing.SiteData;

/// <summary>
/// Writes <c>site-data/corrections.json</c>, the corrections log the methodology page lists (issue #117). Site data
/// only: the file isn't served or downloadable, so it isn't part of the frozen data contract. Field names and order
/// are spelled out here and mirrored by <c>site/src/lib/corrections.js</c>. Entries are listed oldest first; the page
/// shows the newest first. The output is LF-terminated UTF-8 without a BOM, the same on every operating system.
/// </summary>
public sealed class CorrectionsExporter : IExporter
{
    /// <summary>The corrections.json schema this exporter writes; the site rejects any other.</summary>
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
            json.WriteString("opened_on", IsoTime.Date(Correction.LogOpenedOn));
            json.WriteStartArray("corrections");
            foreach (var correction in model.Corrections)
            {
                WriteCorrection(json, correction);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        buffer.Write("\n"u8);
        return [new ExportFile(ExportPaths.SiteCorrections, buffer.WrittenMemory)];
    }

    private static void WriteCorrection(Utf8JsonWriter json, Correction correction)
    {
        json.WriteStartObject();
        json.WriteString("id", correction.Id);
        json.WriteString("reported_on", IsoTime.Date(correction.ReportedOn));
        json.WriteString("corrected_on", IsoTime.Date(correction.CorrectedOn));
        json.WriteStartArray("record_ids");
        foreach (var recordId in correction.RecordIds)
        {
            json.WriteStringValue(recordId);
        }

        json.WriteEndArray();
        json.WriteString("summary", correction.Summary);
        if (correction.Issue is { } issue)
        {
            json.WriteNumber("issue", issue);
        }
        else
        {
            json.WriteNull("issue");
        }

        json.WriteEndObject();
    }
}
