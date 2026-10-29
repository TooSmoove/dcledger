using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>Raw retrieved documents in the private bucket, addressed by SHA-256 (ADR-0002, ADR-0003).</summary>
public interface IDocumentStore
{
    /// <summary>
    /// Stores <paramref name="content"/> under its SHA-256 unless an object is already there; existing objects are
    /// never rewritten. Throws <see cref="StorageFailedException"/> when the bucket can't be reached.
    /// </summary>
    Task<StoredDocument> PutAsync(ReadOnlyMemory<byte> content, string? mediaType, CancellationToken cancellationToken);
}

/// <summary>Where a document is stored, and whether this call wrote it.</summary>
public sealed record StoredDocument(Sha256Hash Sha256, string Key, bool Created);

/// <summary>A meeting as its platform identifies it.</summary>
/// <param name="Platform">Where the meeting comes from.</param>
/// <param name="Client">The platform's name for the jurisdiction, for example the Legistar client <c>guilford</c>.</param>
/// <param name="MeetingId">The platform's meeting ID, for example Legistar's <c>EventId</c>.</param>
public sealed record MeetingKey(Platform Platform, string Client, string MeetingId);

/// <summary>What ingest remembers about one meeting between runs. Private bucket only; never published (SPEC §H).</summary>
public sealed record MeetingState
{
    /// <summary>When an ingest run first saw the meeting's published agenda: T for the review SLA (SPEC AC9).</summary>
    public required DateTimeOffset FirstSeenUtc { get; init; }

    /// <summary>SHA-256 of the agenda as last retrieved, to notice republication.</summary>
    public Sha256Hash? LastAgendaSha256 { get; init; }

    /// <summary>The review issue opened for this agenda, once there is one.</summary>
    public int? ReviewIssueNumber { get; init; }
}

/// <summary>Ingest state per meeting, kept in the private bucket between runs (ADR-0002).</summary>
public interface IIngestState
{
    /// <summary>Returns the meeting's state, or null when ingest hasn't recorded it yet.</summary>
    Task<MeetingState?> GetAsync(MeetingKey meeting, CancellationToken cancellationToken);

    /// <summary>Replaces the meeting's state.</summary>
    Task PutAsync(MeetingKey meeting, MeetingState state, CancellationToken cancellationToken);
}

/// <summary>How an ingest run ended.</summary>
public enum RunOutcome
{
    /// <summary>Every step finished.</summary>
    Succeeded,

    /// <summary>The run stopped on an error; see <see cref="RunRecord.Errors"/>.</summary>
    Failed,
}

/// <summary>One ingest run, as written to the run log.</summary>
public sealed record RunRecord
{
    /// <summary>Unique per run, for example the GitHub Actions run ID and attempt (<c>40623358875-1</c>).</summary>
    public required string RunId { get; init; }

    /// <summary>The jurisdiction ingested.</summary>
    public required string JurisdictionId { get; init; }

    /// <summary>When the run started.</summary>
    public required DateTimeOffset StartedUtc { get; init; }

    /// <summary>When the run finished.</summary>
    public required DateTimeOffset FinishedUtc { get; init; }

    /// <summary>How late the run started against its schedule (SPEC AC9; spike S5). Null when started by hand.</summary>
    public TimeSpan? StartDelay { get; init; }

    /// <summary>How the run ended.</summary>
    public required RunOutcome Outcome { get; init; }

    /// <summary>What went wrong, if anything.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Every document the run retrieved, in order: the run's manifest (WS-AC3).</summary>
    public IReadOnlyList<RetrievedDocument> Documents { get; init; } = [];
}

/// <summary>A document an ingest run retrieved, and where its bytes are kept (one manifest entry).</summary>
public sealed record RetrievedDocument
{
    /// <summary>The meeting the document belongs to.</summary>
    public required MeetingKey Meeting { get; init; }

    /// <summary>Where the bytes came from, after redirects.</summary>
    public required Uri Uri { get; init; }

    /// <summary>When the bytes were read, in UTC.</summary>
    public required DateTimeOffset RetrievedAtUtc { get; init; }

    /// <summary>SHA-256 of the bytes; also their key in the private bucket.</summary>
    public required Sha256Hash Sha256 { get; init; }

    /// <summary>The media type the server gave, or <c>application/octet-stream</c>.</summary>
    public required string MediaType { get; init; }

    /// <summary>Size of the bytes.</summary>
    public required long Bytes { get; init; }

    /// <summary>True when this run stored the bytes; false when the bucket already had them, or in a dry run.</summary>
    public required bool Created { get; init; }
}

/// <summary>Run logs in the private bucket (ADR-0002).</summary>
public interface IRunLog
{
    /// <summary>Writes the run's record. Callers log a failure and carry on: a lost run log never fails a run (ADR-0005).</summary>
    Task WriteAsync(RunRecord run, CancellationToken cancellationToken);
}

/// <summary>The private bucket couldn't be read or written, after the client's own retries (ADR-0005, exit code 3).</summary>
public sealed class StorageFailedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public StorageFailedException(string key, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Key = key;
    }

    /// <summary>Standard constructor; prefer the one that records the key.</summary>
    public StorageFailedException()
        : this(string.Empty, "A storage operation failed.")
    {
    }

    /// <summary>Standard constructor; prefer the one that records the key.</summary>
    public StorageFailedException(string message)
        : this(string.Empty, message)
    {
    }

    /// <summary>Standard constructor; prefer the one that records the key.</summary>
    public StorageFailedException(string message, Exception innerException)
        : this(string.Empty, message, innerException)
    {
    }

    /// <summary>The object key involved.</summary>
    public string Key { get; }
}
