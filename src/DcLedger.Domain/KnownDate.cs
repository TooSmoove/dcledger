using System.Text;

namespace DcLedger.Domain;

/// <summary>Which field of an action a <see cref="KnownDate"/> comes from (ADR-0010 Decision 1).</summary>
public enum KnownDateKind
{
    /// <summary>The action's <see cref="DecisionTerms.ExpiresOn"/>: the last day in force.</summary>
    Expiry,

    /// <summary>The action's <see cref="LocalAction.NextOn"/>: the date a document gives for the body's next step.</summary>
    Next,
}

/// <summary>Where a known date stands at D, the Eastern date of the export (ADR-0010 Decision 3). Exactly one holds.</summary>
public enum KnownDateState
{
    /// <summary>The date is D or later.</summary>
    Upcoming,

    /// <summary>An expiry whose date has passed and whose action is superseded or repealed: it never came to pass as an end.</summary>
    Ended,

    /// <summary>The date has passed and the record shows what happened.</summary>
    Recorded,

    /// <summary>The date has passed and nothing on the record covers it yet.</summary>
    PassedUnrecorded,
}

/// <summary>
/// A date the reviewed ledger states about what comes next (ADR-0010): never computed, never taken from a meeting
/// schedule or a news report's "expected". Derived only from merged YAML.
/// </summary>
public sealed record KnownDate
{
    /// <summary>Which field it comes from.</summary>
    public required KnownDateKind Kind { get; init; }

    /// <summary>The <see cref="LocalAction.Id"/> that states it.</summary>
    public required string ActionId { get; init; }

    /// <summary>The action's jurisdiction.</summary>
    public required string JurisdictionId { get; init; }

    /// <summary>The date, Eastern.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>The stable iCalendar UID (<see cref="KnownDateRules.Uid"/>).</summary>
    public string Uid => KnownDateRules.Uid(Kind, ActionId);

    /// <summary>The row anchor on <c>/ahead/</c>: <c>{action id}-{kind}</c>.</summary>
    public string Anchor => $"{ActionId}-{Kind.ToToken()}";
}

/// <summary>
/// The pure rules for known dates (ADR-0010 Decisions 1, 3, 4 and 5). Given one ledger and one D they always give the
/// same answer; site/src/lib/ahead.js mirrors them for the page, pinned by tests/fixtures/known-dates/cases.json.
/// </summary>
public static class KnownDateRules
{
    private const string UidSuffix = "@dcledger";

    /// <summary>How many days ahead the feed reminds readers: 30 for journalists' planning, 7 for residents (owner, Nov 20, 2026).</summary>
    public static IReadOnlyList<int> ReminderLeadDays { get; } = [30, 7];

    /// <summary>The known dates <paramref name="action"/> states: its expiry, then its next step, each when present.</summary>
    public static IEnumerable<KnownDate> Of(LocalAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Terms.ExpiresOn is { } expiresOn)
        {
            yield return new KnownDate { Kind = KnownDateKind.Expiry, ActionId = action.Id, JurisdictionId = action.JurisdictionId, Date = expiresOn };
        }

        if (action.NextOn is { } nextOn)
        {
            yield return new KnownDate { Kind = KnownDateKind.Next, ActionId = action.Id, JurisdictionId = action.JurisdictionId, Date = nextOn };
        }
    }

    /// <summary>
    /// The state of <paramref name="date"/> at <paramref name="today"/>, checked in the ADR's order: upcoming (the date
    /// is today or later, whatever the action's status), then ended, then recorded, else passed with nothing recorded.
    /// <paramref name="somethingOnDate"/> says whether the same jurisdiction has an event on that Eastern date or an
    /// action decided on it; it only matters for <see cref="KnownDateKind.Next"/>.
    /// </summary>
    public static KnownDateState StateAt(KnownDate date, LocalAction action, bool somethingOnDate, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(date);
        ArgumentNullException.ThrowIfNull(action);
        if (date.Date >= today)
        {
            return KnownDateState.Upcoming;
        }

        if (date.Kind == KnownDateKind.Expiry && action.Terms.Status is ActionStatus.Superseded or ActionStatus.Repealed)
        {
            return KnownDateState.Ended;
        }

        var recorded = date.Kind == KnownDateKind.Expiry
            ? action.Terms.Status == ActionStatus.Expired || action.Terms.SuccessorId is not null
            : somethingOnDate;
        return recorded ? KnownDateState.Recorded : KnownDateState.PassedUnrecorded;
    }

    /// <summary>
    /// <c>&lt;64 hex&gt;@dcledger</c>, a hash of <c>known</c>, the kind and the action ID. The date is left out on purpose, so
    /// a corrected or rescheduled date updates the same calendar entry.
    /// </summary>
    public static string Uid(KnownDateKind kind, string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        return Sha256Hash.Compute(Encoding.UTF8.GetBytes($"known\n{kind.ToToken()}\n{actionId}")).Value + UidSuffix;
    }

    /// <summary>00:00 Eastern on <paramref name="day"/>, the instant every known-date feed item is dated by.</summary>
    public static DateTimeOffset EasternMidnight(DateOnly day)
    {
        // Midnight is never skipped or repeated in America/New_York: the clocks change at 2:00.
        EasternTime.TryToInstant(day.ToDateTime(TimeOnly.MinValue), out var instant, out _);
        return instant;
    }

    /// <summary>The Eastern calendar date of <paramref name="instant"/>: D, for the export's <c>generated_utc</c>.</summary>
    public static DateOnly EasternDate(DateTimeOffset instant) => DateOnly.FromDateTime(EasternTime.ToEastern(instant).DateTime);

    /// <summary>
    /// Whether the reminder <paramref name="leadDays"/> ahead of <paramref name="date"/> is in the feed at
    /// <paramref name="today"/>: its day (<c>date - lead</c>) has come, and its 00:00 Eastern is not before the action's
    /// own <paramref name="actionPublished"/> (its newest document retrieval), whose feed item already announced the date.
    /// </summary>
    public static bool ReminderIsDue(KnownDate date, int leadDays, DateOnly today, DateTimeOffset actionPublished)
    {
        ArgumentNullException.ThrowIfNull(date);
        var day = date.Date.AddDays(-leadDays);
        return day <= today && EasternMidnight(day) >= actionPublished;
    }
}
