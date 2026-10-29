using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DcLedger.Application;

namespace DcLedger.Infrastructure.Export;

/// <summary>
/// Reads <c>ingest-status.json</c>, which publish builds from ingest run history for <c>dcl export --status</c>
/// (ADR-0004). Strict, like the other state files: unknown or missing fields, non-UTC times and duplicate IDs are
/// errors, never silently defaulted.
/// <code>
/// {
///   "schema_version": 1,
///   "generated_utc": "2026-10-29T14:47:00Z",
///   "jurisdictions": [ { "id": "guilford", "last_checked_utc": "2026-10-29T12:17:43Z" } ]
/// }
/// </code>
/// <c>last_checked_utc</c> is when the newest successful ingest run checked the jurisdiction, or null when the history
/// has none.
/// </summary>
public static partial class IngestStatusFile
{
    /// <summary>The only schema version this reader accepts.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Size cap; a real file is a few hundred bytes.</summary>
    public const int MaxBytes = 1024 * 1024;

    private static readonly string[] UtcFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];

    /// <summary>Reads and checks the file at <paramref name="path"/>. Throws <see cref="IngestStatusException"/> when it can't.</summary>
    public static IngestStatus Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] bytes;
        try
        {
            if (new FileInfo(path).Length > MaxBytes)
            {
                throw new IngestStatusException($"{path} is larger than {MaxBytes} bytes.");
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IngestStatusException($"Can't read the ingest status {path}: {exception.Message}", exception);
        }

        return Parse(bytes, path);
    }

    /// <summary>Checks UTF-8 JSON; <paramref name="source"/> names it in error messages.</summary>
    public static IngestStatus Parse(ReadOnlySpan<byte> utf8Json, string source)
    {
        IngestStatusJson? json;
        try
        {
            json = JsonSerializer.Deserialize(utf8Json, IngestStatusJsonContext.Default.IngestStatusJson);
        }
        catch (JsonException exception)
        {
            throw Invalid(source, exception.Message, exception);
        }

        if (json is null)
        {
            throw Invalid(source, "expected a JSON object.");
        }

        if (json.SchemaVersion != SchemaVersion)
        {
            throw Invalid(source, $"schema_version is {json.SchemaVersion.ToString(CultureInfo.InvariantCulture)}; expected {SchemaVersion.ToString(CultureInfo.InvariantCulture)}.");
        }

        var lastChecked = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
        foreach (var jurisdiction in json.Jurisdictions)
        {
            if (!Slug().IsMatch(jurisdiction.Id))
            {
                throw Invalid(source, $"'{jurisdiction.Id}' isn't a jurisdiction ID (lowercase letters, digits and single hyphens).");
            }

            var checkedAt = jurisdiction.LastCheckedUtc is { } text ? Utc(text, source, $"last_checked_utc of '{jurisdiction.Id}'") : (DateTimeOffset?)null;
            if (!lastChecked.TryAdd(jurisdiction.Id, checkedAt))
            {
                throw Invalid(source, $"'{jurisdiction.Id}' is listed more than once.");
            }
        }

        return new IngestStatus
        {
            GeneratedUtc = Utc(json.GeneratedUtc, source, "generated_utc"),
            LastCheckedUtc = lastChecked,
        };
    }

    private static DateTimeOffset Utc(string text, string source, string field) =>
        DateTimeOffset.TryParseExact(text, UtcFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : throw Invalid(source, $"{field} '{text}' isn't a UTC time like 2026-10-29T14:47:00Z.");

    private static IngestStatusException Invalid(string source, string reason, Exception? inner = null) =>
        new($"{source} isn't a valid ingest status: {reason}", inner);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Slug();
}

/// <summary>The ingest status file is missing, unreadable or malformed: a configuration error (exit code 2, ADR-0004).</summary>
public sealed class IngestStatusException : Exception
{
    /// <summary>Creates the exception.</summary>
    public IngestStatusException()
        : this("The ingest status is invalid.")
    {
    }

    /// <summary>Creates the exception.</summary>
    public IngestStatusException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public IngestStatusException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

internal sealed record IngestStatusJson(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("generated_utc")] string GeneratedUtc,
    [property: JsonPropertyName("jurisdictions")] IReadOnlyList<IngestStatusJurisdictionJson> Jurisdictions);

internal sealed record IngestStatusJurisdictionJson(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("last_checked_utc")] string? LastCheckedUtc);

[JsonSourceGenerationOptions(
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(IngestStatusJson))]
internal sealed partial class IngestStatusJsonContext : JsonSerializerContext;
