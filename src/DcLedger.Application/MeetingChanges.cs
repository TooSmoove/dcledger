namespace DcLedger.Application;

/// <summary>
/// What ingest last saw of a tracked meeting's schedule (issue #29), so a later run notices a new date or time, a
/// cancellation hint or a withdrawn agenda. Holds no source text: the comment that hints at a cancellation can name
/// people, so only the fact that it mentions one is kept.
/// </summary>
public sealed record MeetingSighting
{
    /// <summary>The meeting date (Eastern).</summary>
    public required DateOnly Date { get; init; }

    /// <summary>The start time (Eastern wall clock), or null when the source gives none.</summary>
    public TimeOnly? Time { get; init; }

    /// <summary>The source hinted at a cancellation (<see cref="SourceMeeting.PossiblyCancelled"/>).</summary>
    public bool PossiblyCancelled { get; init; }

    /// <summary>The platform listed a published agenda.</summary>
    public bool AgendaPublished { get; init; }

    /// <summary>What <paramref name="meeting"/> looks like now.</summary>
    public static MeetingSighting Of(SourceMeeting meeting)
    {
        ArgumentNullException.ThrowIfNull(meeting);
        return new MeetingSighting
        {
            Date = meeting.Date,
            Time = meeting.Time,
            PossiblyCancelled = meeting.PossiblyCancelled,
            AgendaPublished = meeting.AgendaUri is not null,
        };
    }
}

/// <summary>
/// A meeting with a review issue, as ingest last listed it. A jurisdiction's tracked meetings are kept together so a run
/// can notice one the platform stops listing before its date (spike S15).
/// </summary>
public sealed record TrackedMeeting
{
    /// <summary>The meeting's platform identity.</summary>
    public required MeetingKey Meeting { get; init; }

    /// <summary>The body, as configured.</summary>
    public required string Body { get; init; }

    /// <summary>The meeting date (Eastern) when last listed.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>The meeting's public page, when last listed.</summary>
    public Uri? MeetingPageUri { get; init; }
}

/// <summary>Something about a tracked meeting that changed since ingest last saw it (issue #29).</summary>
public enum MeetingChange
{
    /// <summary>The date or the start time changed.</summary>
    Rescheduled,

    /// <summary>A comment on the meeting now mentions a cancellation.</summary>
    CancellationMentioned,

    /// <summary>The comment that mentioned a cancellation is gone.</summary>
    CancellationMentionRemoved,

    /// <summary>The platform no longer lists a published agenda.</summary>
    AgendaWithdrawn,

    /// <summary>The platform lists a published agenda again.</summary>
    AgendaPublishedAgain,

    /// <summary>The platform no longer lists the meeting, and its date hasn't passed.</summary>
    NoLongerListed,

    /// <summary>The platform lists the meeting again after it went missing.</summary>
    ListedAgain,
}

/// <summary>Everything a meeting-changed comment says. No source text, like <see cref="ReviewCandidate"/>.</summary>
public sealed record MeetingChangeNotice
{
    /// <summary>The meeting's platform identity.</summary>
    public required MeetingKey Meeting { get; init; }

    /// <summary>The jurisdiction's display name.</summary>
    public required string JurisdictionName { get; init; }

    /// <summary>The body, as configured.</summary>
    public required string Body { get; init; }

    /// <summary>The meeting's public page, when known.</summary>
    public Uri? MeetingPageUri { get; init; }

    /// <summary>The meeting as ingest saw it before.</summary>
    public required MeetingSighting Before { get; init; }

    /// <summary>The meeting as listed now; null when the platform no longer lists it.</summary>
    public MeetingSighting? Now { get; init; }

    /// <summary>What changed, in the order the comment lists it.</summary>
    public required IReadOnlyList<MeetingChange> Changes { get; init; }

    /// <summary>When the change was noticed.</summary>
    public required DateTimeOffset CheckedUtc { get; init; }
}

/// <summary>Compares sightings of a meeting. Nothing here decides that a meeting is cancelled; a human does (spike S15).</summary>
public static class MeetingChanges
{
    /// <summary>What changed from <paramref name="before"/> to <paramref name="now"/>, in a fixed order; empty when nothing did.</summary>
    public static IReadOnlyList<MeetingChange> Between(MeetingSighting before, MeetingSighting now)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(now);
        var changes = new List<MeetingChange>();
        if (before.Date != now.Date || before.Time != now.Time)
        {
            changes.Add(MeetingChange.Rescheduled);
        }

        if (before.PossiblyCancelled != now.PossiblyCancelled)
        {
            changes.Add(now.PossiblyCancelled ? MeetingChange.CancellationMentioned : MeetingChange.CancellationMentionRemoved);
        }

        if (before.AgendaPublished != now.AgendaPublished)
        {
            changes.Add(now.AgendaPublished ? MeetingChange.AgendaPublishedAgain : MeetingChange.AgendaWithdrawn);
        }

        return changes;
    }

    /// <summary>True when any change hints the meeting may be off.</summary>
    public static bool SuggestsCancellation(IEnumerable<MeetingChange> changes) =>
        changes.Any(change => change is MeetingChange.CancellationMentioned or MeetingChange.AgendaWithdrawn or MeetingChange.NoLongerListed);

    /// <summary>True when any change hints a meeting thought to be off is back on.</summary>
    public static bool SuggestsReinstatement(IEnumerable<MeetingChange> changes) =>
        changes.Any(change => change is MeetingChange.CancellationMentionRemoved or MeetingChange.AgendaPublishedAgain or MeetingChange.ListedAgain);

    /// <summary>A fixed token for logs and the comment's hidden marker, for example <c>no-longer-listed</c>.</summary>
    public static string Token(MeetingChange change) => change switch
    {
        MeetingChange.Rescheduled => "rescheduled",
        MeetingChange.CancellationMentioned => "cancellation-mentioned",
        MeetingChange.CancellationMentionRemoved => "cancellation-mention-removed",
        MeetingChange.AgendaWithdrawn => "agenda-withdrawn",
        MeetingChange.AgendaPublishedAgain => "agenda-published-again",
        MeetingChange.NoLongerListed => "no-longer-listed",
        MeetingChange.ListedAgain => "listed-again",
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown meeting change."),
    };
}
