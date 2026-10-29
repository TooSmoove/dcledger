using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>One ingest run for one jurisdiction.</summary>
public sealed record IngestRequest
{
    /// <summary>The jurisdiction, as configured in the ledger data.</summary>
    public required Jurisdiction Jurisdiction { get; init; }

    /// <summary>Unique per run and jurisdiction, for example the Actions run ID and attempt (<c>40623358875-1</c>).</summary>
    public required string RunId { get; init; }

    /// <summary>When the schedule meant the run to start, to log how late it started (SPEC AC9). Null when started by hand.</summary>
    public DateTimeOffset? ScheduledForUtc { get; init; }

    /// <summary>
    /// Read the source and download matching agendas, but read no ingest state, store nothing, open or comment on no
    /// issue and write no run log. Needs no credentials.
    /// </summary>
    public bool DryRun { get; init; }
}

/// <summary>What ingest did with one meeting.</summary>
public enum MeetingOutcome
{
    /// <summary>No agenda is published yet, so the review clock hasn't started.</summary>
    NoAgenda,

    /// <summary>The agenda is published and no item matches the keyword rules; only its first-seen time is recorded.</summary>
    NoMatch,

    /// <summary>A review issue was opened.</summary>
    IssueOpened,

    /// <summary>A dry run would have opened a review issue.</summary>
    WouldOpenIssue,

    /// <summary>The agenda's bytes changed since the issue was opened; the issue got a comment.</summary>
    AgendaChanged,

    /// <summary>The meeting already has an issue and its agenda is unchanged.</summary>
    Unchanged,

    /// <summary>The agenda couldn't be downloaded; see <see cref="MeetingResult.Error"/>.</summary>
    Failed,
}

/// <summary>One meeting's outcome. Holds no source titles, so it is safe to print in public logs.</summary>
public sealed record MeetingResult
{
    /// <summary>The meeting's platform identity.</summary>
    public required MeetingKey Meeting { get; init; }

    /// <summary>The meeting date (Eastern).</summary>
    public required DateOnly Date { get; init; }

    /// <summary>The body, as configured.</summary>
    public required string Body { get; init; }

    /// <summary>What happened.</summary>
    public required MeetingOutcome Outcome { get; init; }

    /// <summary>The keyword rules the agenda's items matched, in rule order.</summary>
    public IReadOnlyList<string> Rules { get; init; } = [];

    /// <summary>The meeting's review issue, when it has one.</summary>
    public int? IssueNumber { get; init; }

    /// <summary>The review issue's title (neutral, never a source title), when one was opened or would be.</summary>
    public string? IssueTitle { get; init; }

    /// <summary>True when commenting reopened a closed issue.</summary>
    public bool Reopened { get; init; }

    /// <summary>Why the meeting failed, for <see cref="MeetingOutcome.Failed"/>.</summary>
    public string? Error { get; init; }
}

/// <summary>The outcome of <see cref="IngestJurisdiction"/>.</summary>
public sealed record IngestReport
{
    /// <summary>The jurisdiction ingested.</summary>
    public required string JurisdictionId { get; init; }

    /// <summary>The first meeting date scanned (Eastern).</summary>
    public required DateOnly FirstDate { get; init; }

    /// <summary>The last meeting date scanned (Eastern).</summary>
    public required DateOnly LastDate { get; init; }

    /// <summary>True for a dry run.</summary>
    public bool DryRun { get; init; }

    /// <summary>How late the run started against its schedule, when it was scheduled.</summary>
    public TimeSpan? StartDelay { get; init; }

    /// <summary>Every meeting the source returned, in its order.</summary>
    public IReadOnlyList<MeetingResult> Meetings { get; init; } = [];

    /// <summary>The documents retrieved (the run log's manifest).</summary>
    public IReadOnlyList<RetrievedDocument> Documents { get; init; } = [];

    /// <summary>What failed. Any error means the jurisdiction counts as "not checked" for this run (ADR-0005).</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Why the run log couldn't be written. Logged, but never fails the run (ADR-0005).</summary>
    public string? RunLogError { get; init; }

    /// <summary>True when nothing failed.</summary>
    public bool Succeeded => Errors.Count == 0;
}
