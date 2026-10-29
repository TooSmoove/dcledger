using System.Text.Json;
using System.Text.Json.Serialization;
using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Infrastructure.Storage;

/// <summary>
/// JSON for ingest state and run logs. Field names are spelled out so renaming a C# property can't change the stored
/// format; unknown or missing fields are errors.
/// </summary>
internal static class StorageJson
{
    private const int SchemaVersion = 1;

    public static byte[] Serialize(MeetingState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var json = new MeetingStateJson(
            SchemaVersion,
            state.FirstSeenUtc.ToUniversalTime(),
            state.LastAgendaSha256?.Value,
            state.ReviewIssueNumber);
        return JsonSerializer.SerializeToUtf8Bytes(json, StorageJsonContext.Default.MeetingStateJson);
    }

    public static MeetingState DeserializeMeetingState(string key, byte[] utf8Json)
    {
        MeetingStateJson? json;
        try
        {
            json = JsonSerializer.Deserialize(utf8Json, StorageJsonContext.Default.MeetingStateJson);
        }
        catch (JsonException exception)
        {
            throw Invalid(key, exception.Message, exception);
        }

        if (json is null || json.SchemaVersion != SchemaVersion)
        {
            throw Invalid(key, $"expected schema_version {SchemaVersion}.");
        }

        Sha256Hash? lastAgenda = null;
        if (json.LastAgendaSha256 is not null)
        {
            if (!Sha256Hash.TryParse(json.LastAgendaSha256, out var parsed))
            {
                throw Invalid(key, "last_agenda_sha256 isn't 64 lowercase hex characters.");
            }

            lastAgenda = parsed;
        }

        return new MeetingState
        {
            FirstSeenUtc = json.FirstSeenUtc.ToUniversalTime(),
            LastAgendaSha256 = lastAgenda,
            ReviewIssueNumber = json.ReviewIssueNumber,
        };
    }

    public static byte[] Serialize(RunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var json = new RunRecordJson(
            SchemaVersion,
            run.RunId,
            run.JurisdictionId,
            run.StartedUtc.ToUniversalTime(),
            run.FinishedUtc.ToUniversalTime(),
            run.StartDelay?.TotalSeconds,
            run.Outcome switch
            {
                RunOutcome.Succeeded => "succeeded",
                RunOutcome.Failed => "failed",
                _ => throw new ArgumentOutOfRangeException(nameof(run), run.Outcome, "Unknown outcome."),
            },
            run.Errors,
            [.. run.Documents.Select(ToJson)]);
        return JsonSerializer.SerializeToUtf8Bytes(json, StorageJsonContext.Default.RunRecordJson);
    }

    private static RetrievedDocumentJson ToJson(RetrievedDocument document) => new(
        document.Meeting.Platform.ToToken(),
        document.Meeting.Client,
        document.Meeting.MeetingId,
        document.Uri.AbsoluteUri,
        document.RetrievedAtUtc.ToUniversalTime(),
        document.Sha256.Value,
        document.MediaType,
        document.Bytes,
        document.Created);

    private static StorageFailedException Invalid(string key, string reason, Exception? inner = null) =>
        new(key, $"{key} isn't valid ingest state: {reason}", inner);
}

internal sealed record MeetingStateJson(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("first_seen_utc")] DateTimeOffset FirstSeenUtc,
    [property: JsonPropertyName("last_agenda_sha256")] string? LastAgendaSha256,
    [property: JsonPropertyName("review_issue_number")] int? ReviewIssueNumber);

internal sealed record RunRecordJson(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("jurisdiction_id")] string JurisdictionId,
    [property: JsonPropertyName("started_utc")] DateTimeOffset StartedUtc,
    [property: JsonPropertyName("finished_utc")] DateTimeOffset FinishedUtc,
    [property: JsonPropertyName("start_delay_seconds")] double? StartDelaySeconds,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors,
    [property: JsonPropertyName("documents")] IReadOnlyList<RetrievedDocumentJson> Documents);

internal sealed record RetrievedDocumentJson(
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("client")] string Client,
    [property: JsonPropertyName("meeting_id")] string MeetingId,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("retrieved_utc")] DateTimeOffset RetrievedUtc,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("media_type")] string MediaType,
    [property: JsonPropertyName("bytes")] long Bytes,
    [property: JsonPropertyName("created")] bool Created);

[JsonSourceGenerationOptions(
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(MeetingStateJson))]
[JsonSerializable(typeof(RunRecordJson))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;
