using System.Diagnostics.CodeAnalysis;
using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>The retrieved agenda a review issue is about.</summary>
/// <param name="Sha256">SHA-256 of the bytes in the private bucket; also the document record's ID.</param>
/// <param name="Uri">Where the bytes came from (after redirects).</param>
/// <param name="MediaType">The media type the server gave, or <c>application/octet-stream</c>.</param>
/// <param name="Bytes">Size of the retrieved bytes.</param>
/// <param name="RetrievedAtUtc">When the bytes were read.</param>
public sealed record AgendaDocument(Sha256Hash Sha256, Uri Uri, string MediaType, long Bytes, DateTimeOffset RetrievedAtUtc);

/// <summary>An agenda item the classifier matched, without its private text: no title and no matter name.</summary>
/// <param name="AgendaNumber">The item's number on the agenda.</param>
/// <param name="CaseNumber">The source's case number, unfiltered (the renderer filters it).</param>
/// <param name="MatterType">The matter type, for example "Public Hearing".</param>
/// <param name="Rules">The classifier rules that matched.</param>
public sealed record ReviewItem(string? AgendaNumber, string? CaseNumber, string? MatterType, IReadOnlyList<string> Rules)
{
    /// <summary>Keeps only the item's public fields.</summary>
    public static ReviewItem FromMatch(SourceItem item, Classification classification)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(classification);
        return new ReviewItem(item.AgendaNumber, item.CaseNumber, item.MatterType, classification.MatchedRules);
    }
}

/// <summary>
/// Everything a review issue may say about a meeting. It carries no source titles, so the issue can't leak them
/// (ADR-0003); every text field is still treated as untrusted when rendered.
/// </summary>
public sealed record ReviewCandidate
{
    /// <summary>The jurisdiction's ID.</summary>
    public required string JurisdictionId { get; init; }

    /// <summary>The jurisdiction's display name.</summary>
    public required string JurisdictionName { get; init; }

    /// <summary>The meeting's platform identity.</summary>
    public required MeetingKey Meeting { get; init; }

    /// <summary>The body, as configured.</summary>
    public required string Body { get; init; }

    /// <summary>The meeting date (Eastern).</summary>
    public required DateOnly Date { get; init; }

    /// <summary>The start time (Eastern wall clock), when the source gives one.</summary>
    public TimeOnly? Time { get; init; }

    /// <summary>The start as an Eastern instant, when <see cref="Time"/> is a valid Eastern time.</summary>
    public DateTimeOffset? StartsAt { get; init; }

    /// <summary>The meeting's public page.</summary>
    public Uri? MeetingPageUri { get; init; }

    /// <summary>The source hints at a cancellation.</summary>
    public bool PossiblyCancelled { get; init; }

    /// <summary>The retrieved agenda.</summary>
    public required AgendaDocument Agenda { get; init; }

    /// <summary>The items that matched, in agenda order.</summary>
    public required IReadOnlyList<ReviewItem> Items { get; init; }

    /// <summary>When ingest first saw the published agenda (SPEC AC9).</summary>
    public required DateTimeOffset FirstSeenUtc { get; init; }

    /// <summary>Other platform meetings with the same body, date and time; the API can list ones the public site hides.</summary>
    public IReadOnlyList<string> SameSlotMeetingIds { get; init; } = [];

    /// <summary>Builds a candidate from a source meeting, dropping its private text.</summary>
    public static ReviewCandidate FromMeeting(
        Jurisdiction jurisdiction,
        SourceMeeting meeting,
        AgendaDocument agenda,
        IReadOnlyList<ReviewItem> items,
        DateTimeOffset firstSeenUtc,
        IReadOnlyList<string> sameSlotMeetingIds)
    {
        ArgumentNullException.ThrowIfNull(jurisdiction);
        ArgumentNullException.ThrowIfNull(meeting);
        return new ReviewCandidate
        {
            JurisdictionId = jurisdiction.Id,
            JurisdictionName = jurisdiction.Name,
            Meeting = meeting.Key,
            Body = meeting.Body,
            Date = meeting.Date,
            Time = meeting.Time,
            StartsAt = meeting.StartsAt,
            MeetingPageUri = meeting.MeetingPageUri,
            PossiblyCancelled = meeting.PossiblyCancelled,
            Agenda = agenda,
            Items = items,
            FirstSeenUtc = firstSeenUtc,
            SameSlotMeetingIds = sameSlotMeetingIds,
        };
    }
}

/// <summary>A rendered review issue.</summary>
public sealed record ReviewIssue(string Title, string Body);

/// <summary>Where review issues go: GitHub issues in this repository (ADR-0004).</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The port's name in ADR-0004: a queue of review work, not a Queue<T>.")]
public interface IReviewQueue
{
    /// <summary>Opens an issue and returns its number.</summary>
    Task<int> OpenAsync(ReviewIssue issue, CancellationToken cancellationToken);

    /// <summary>Comments on an issue, reopening it first if it's closed. Returns true when it was reopened.</summary>
    Task<bool> CommentAsync(int issueNumber, string body, CancellationToken cancellationToken);
}

/// <summary>The review queue couldn't be reached or refused a request (ADR-0005, exit code 3).</summary>
public sealed class ReviewQueueFailedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ReviewQueueFailedException(string message, int? statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>Standard constructor.</summary>
    public ReviewQueueFailedException()
        : this("The review queue request failed.", statusCode: null)
    {
    }

    /// <summary>Standard constructor.</summary>
    public ReviewQueueFailedException(string message)
        : this(message, statusCode: null)
    {
    }

    /// <summary>Standard constructor.</summary>
    public ReviewQueueFailedException(string message, Exception innerException)
        : this(message, null, innerException)
    {
    }

    /// <summary>The HTTP status, when the server answered.</summary>
    public int? StatusCode { get; }
}
