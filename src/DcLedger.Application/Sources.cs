using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>Reads meetings from a jurisdiction's agenda platform (ADR-0004). One implementation per platform.</summary>
public interface ISourceAdapter
{
    /// <summary>The platform this adapter reads.</summary>
    Platform Platform { get; }

    /// <summary>
    /// Meetings of the jurisdiction's configured bodies dated from <paramref name="startDate"/> up to, not including,
    /// <paramref name="endDate"/>, with their agenda items, ordered by date, time and meeting ID. Throws
    /// <see cref="SourceFailedException"/> when the source can't be read or its data isn't shaped as expected.
    /// </summary>
    Task<IReadOnlyList<SourceMeeting>> GetMeetingsAsync(Jurisdiction jurisdiction, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken);
}

/// <summary>A meeting as the source reports it. Untrusted input until a reviewer approves the ledger record.</summary>
public sealed record SourceMeeting
{
    /// <summary>The meeting's platform identity, also its ingest-state key.</summary>
    public required MeetingKey Key { get; init; }

    /// <summary>The body, as configured for the jurisdiction (source whitespace trimmed).</summary>
    public required string Body { get; init; }

    /// <summary>The meeting date (Eastern).</summary>
    public required DateOnly Date { get; init; }

    /// <summary>The start time (Eastern wall clock), or null when the source gives none.</summary>
    public TimeOnly? Time { get; init; }

    /// <summary>
    /// The start as an Eastern instant. Null when <see cref="Time"/> is missing or the wall time is skipped or repeated
    /// by daylight saving, which is never guessed (spike S6); a reviewer supplies it.
    /// </summary>
    public DateTimeOffset? StartsAt { get; init; }

    /// <summary>The published agenda document, or null until there is one.</summary>
    public Uri? AgendaUri { get; init; }

    /// <summary>The meeting's page on the platform's public site.</summary>
    public Uri? MeetingPageUri { get; init; }

    /// <summary>
    /// The source hints the meeting is cancelled (a comment mentions it). The platform has no cancellation status
    /// (spike S15), so a human confirms; nothing is marked cancelled automatically.
    /// </summary>
    public bool PossiblyCancelled { get; init; }

    /// <summary>Agenda items in agenda order; empty before the agenda is built.</summary>
    public IReadOnlyList<SourceItem> Items { get; init; } = [];
}

/// <summary>One agenda item as the source reports it.</summary>
public sealed record SourceItem
{
    /// <summary>The platform's item ID.</summary>
    public required string ItemId { get; init; }

    /// <summary>The item's number on the agenda, for example <c>A.</c>.</summary>
    public string? AgendaNumber { get; init; }

    /// <summary>
    /// The source's title. Untrusted and private: it can name people, so it is never published or put in an issue
    /// (SPEC §H); only the classifier reads it.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>The matter's name, when the item belongs to one. Private like <see cref="Title"/>.</summary>
    public string? MatterName { get; init; }

    /// <summary>The source's case or file number, unfiltered.</summary>
    public string? CaseNumber { get; init; }

    /// <summary>The matter type, for example "Public Hearing".</summary>
    public string? MatterType { get; init; }

    /// <summary>What the keyword classifier reads: title, matter name, case number and matter type.</summary>
    public string ClassifierText => string.Join(" | ", Title, MatterName, CaseNumber, MatterType);
}

/// <summary>Why reading a source failed (ADR-0005).</summary>
public enum SourceFailure
{
    /// <summary>The source is down, timing out or erroring, after retries. Try again next run.</summary>
    Unavailable,

    /// <summary>The source refused us: robots.txt, or a 4xx status such as 403 or 404. Needs a human.</summary>
    Rejected,

    /// <summary>The response isn't shaped as expected: the platform changed or the adapter is wrong. Needs a human.</summary>
    FormatChanged,
}

/// <summary>A source couldn't be read. The jurisdiction is "not checked" for this run (ADR-0005, exit code 3).</summary>
public sealed class SourceFailedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SourceFailedException(SourceFailure failure, Uri? uri, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        Uri = uri;
    }

    /// <summary>Standard constructor; prefer the one that records the failure.</summary>
    public SourceFailedException()
        : this(SourceFailure.Unavailable, null, "The source couldn't be read.")
    {
    }

    /// <summary>Standard constructor; prefer the one that records the failure.</summary>
    public SourceFailedException(string message)
        : this(SourceFailure.Unavailable, null, message)
    {
    }

    /// <summary>Standard constructor; prefer the one that records the failure.</summary>
    public SourceFailedException(string message, Exception innerException)
        : this(SourceFailure.Unavailable, null, message, innerException)
    {
    }

    /// <summary>Why it failed.</summary>
    public SourceFailure Failure { get; }

    /// <summary>The request that failed, when there was one.</summary>
    public Uri? Uri { get; }
}
