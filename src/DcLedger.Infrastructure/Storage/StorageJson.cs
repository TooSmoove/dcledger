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
            state.ReviewIssueNumber,
            state.LastSeen is { } seen ? new MeetingSightingJson(seen.Date, seen.Time, seen.PossiblyCancelled, seen.AgendaPublished) : null,
            state.MissingSinceUtc?.ToUniversalTime());
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
            LastSeen = json.LastSeen is { } seen
                ? new MeetingSighting { Date = seen.Date, Time = seen.Time, PossiblyCancelled = seen.PossiblyCancelled, AgendaPublished = seen.AgendaPublished }
                : null,
            MissingSinceUtc = json.MissingSinceUtc?.ToUniversalTime(),
        };
    }

    public static byte[] SerializeTracked(IReadOnlyList<TrackedMeeting> meetings)
    {
        ArgumentNullException.ThrowIfNull(meetings);
        var json = new TrackedMeetingsJson(
            SchemaVersion,
            [.. meetings.Select(entry => new TrackedMeetingJson(
                entry.Meeting.Platform.ToToken(),
                entry.Meeting.Client,
                entry.Meeting.MeetingId,
                entry.Body,
                entry.Date,
                entry.MeetingPageUri?.AbsoluteUri))]);
        return JsonSerializer.SerializeToUtf8Bytes(json, StorageJsonContext.Default.TrackedMeetingsJson);
    }

    public static IReadOnlyList<TrackedMeeting> DeserializeTracked(string key, byte[] utf8Json)
    {
        TrackedMeetingsJson? json;
        try
        {
            json = JsonSerializer.Deserialize(utf8Json, StorageJsonContext.Default.TrackedMeetingsJson);
        }
        catch (JsonException exception)
        {
            throw Invalid(key, exception.Message, exception);
        }

        if (json is null || json.SchemaVersion != SchemaVersion)
        {
            throw Invalid(key, $"expected schema_version {SchemaVersion}.");
        }

        var meetings = new List<TrackedMeeting>();
        foreach (var entry in json.Meetings)
        {
            var platform = entry.Platform switch
            {
                "legistar" => Platform.Legistar,
                "manual" => Platform.Manual,
                _ => throw Invalid(key, $"unknown platform '{entry.Platform}'."),
            };
            Uri? page = null;
            if (entry.MeetingPageUrl is not null && !Uri.TryCreate(entry.MeetingPageUrl, UriKind.Absolute, out page))
            {
                throw Invalid(key, "meeting_page_url isn't an absolute URL.");
            }

            meetings.Add(new TrackedMeeting
            {
                Meeting = new MeetingKey(platform, entry.Client, entry.MeetingId),
                Body = entry.Body,
                Date = entry.Date,
                MeetingPageUri = page,
            });
        }

        return meetings;
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

// last_seen and missing_since_utc (issue #29) are left out while null, so state written before them reads the same and
// its documented shape is unchanged.
internal sealed record MeetingStateJson(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("first_seen_utc")] DateTimeOffset FirstSeenUtc,
    [property: JsonPropertyName("last_agenda_sha256")] string? LastAgendaSha256,
    [property: JsonPropertyName("review_issue_number")] int? ReviewIssueNumber,
    [property: JsonPropertyName("last_seen"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MeetingSightingJson? LastSeen = null,
    [property: JsonPropertyName("missing_since_utc"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? MissingSinceUtc = null);

internal sealed record MeetingSightingJson(
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("time")] TimeOnly? Time,
    [property: JsonPropertyName("possibly_cancelled")] bool PossiblyCancelled,
    [property: JsonPropertyName("agenda_published")] bool AgendaPublished);

internal sealed record TrackedMeetingsJson(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("meetings")] IReadOnlyList<TrackedMeetingJson> Meetings);

internal sealed record TrackedMeetingJson(
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("client")] string Client,
    [property: JsonPropertyName("meeting_id")] string MeetingId,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("meeting_page_url")] string? MeetingPageUrl);

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
[JsonSerializable(typeof(TrackedMeetingsJson))]
[JsonSerializable(typeof(RunRecordJson))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;
