namespace DcLedger.Domain;

/// <summary>
/// Fixed lowercase tokens for the enums that appear in YAML and in exported files (events.json, CSV). They are part of
/// the public data contract (ADR-0004), so renaming an enum member can't change them.
/// </summary>
public static class EnumTokens
{
    /// <summary>Returns <c>scheduled</c> or <c>cancelled</c>.</summary>
    public static string ToToken(this EventStatus status) => status switch
    {
        EventStatus.Scheduled => "scheduled",
        EventStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown event status."),
    };

    /// <summary>Returns <c>reported_news</c> or <c>confirmed_official</c>.</summary>
    public static string ToToken(this EvidenceLabel label) => label switch
    {
        EvidenceLabel.ReportedNews => "reported_news",
        EvidenceLabel.ConfirmedOfficial => "confirmed_official",
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown evidence label."),
    };

    /// <summary>Returns <c>official</c> or <c>news</c>.</summary>
    public static string ToToken(this SourceType sourceType) => sourceType switch
    {
        SourceType.Official => "official",
        SourceType.News => "news",
        _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, "Unknown source type."),
    };

    /// <summary>Returns <c>fetched</c> or <c>manual</c>.</summary>
    public static string ToToken(this Retrieval retrieval) => retrieval switch
    {
        Retrieval.Fetched => "fetched",
        Retrieval.Manual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(retrieval), retrieval, "Unknown retrieval."),
    };

    /// <summary>
    /// Returns <c>minutes</c>, <c>legislation</c>, <c>agenda</c>, <c>agenda_packet</c>, <c>notice</c>,
    /// <c>news_release</c>, <c>news_article</c> or <c>other</c>.
    /// </summary>
    public static string ToToken(this DocumentKind kind) => kind switch
    {
        DocumentKind.Minutes => "minutes",
        DocumentKind.Legislation => "legislation",
        DocumentKind.Agenda => "agenda",
        DocumentKind.AgendaPacket => "agenda_packet",
        DocumentKind.Notice => "notice",
        DocumentKind.NewsRelease => "news_release",
        DocumentKind.NewsArticle => "news_article",
        DocumentKind.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown document kind."),
    };

    /// <summary>Returns <c>moratorium</c>, <c>rezoning</c>, <c>text_amendment</c> or <c>resolution</c>.</summary>
    public static string ToToken(this ActionType actionType) => actionType switch
    {
        ActionType.Moratorium => "moratorium",
        ActionType.Rezoning => "rezoning",
        ActionType.TextAmendment => "text_amendment",
        ActionType.Resolution => "resolution",
        _ => throw new ArgumentOutOfRangeException(nameof(actionType), actionType, "Unknown action type."),
    };

    /// <summary>Returns <c>adopted</c>, <c>adopted_amended</c>, <c>failed</c>, <c>continued</c> or <c>tabled</c>.</summary>
    public static string ToToken(this ActionOutcome outcome) => outcome switch
    {
        ActionOutcome.Adopted => "adopted",
        ActionOutcome.AdoptedAmended => "adopted_amended",
        ActionOutcome.Failed => "failed",
        ActionOutcome.Continued => "continued",
        ActionOutcome.Tabled => "tabled",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown action outcome."),
    };

    /// <summary>Returns <c>watched</c>, <c>decisions</c> or <c>registered</c>.</summary>
    public static string ToToken(this CoverageTier tier) => tier switch
    {
        CoverageTier.Watched => "watched",
        CoverageTier.Decisions => "decisions",
        CoverageTier.Registered => "registered",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown coverage tier."),
    };

    /// <summary>Returns <c>in_effect</c>, <c>expired</c>, <c>superseded</c> or <c>repealed</c>.</summary>
    public static string ToToken(this ActionStatus status) => status switch
    {
        ActionStatus.InEffect => "in_effect",
        ActionStatus.Expired => "expired",
        ActionStatus.Superseded => "superseded",
        ActionStatus.Repealed => "repealed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown action status."),
    };

    /// <summary>Returns <c>expiry</c> or <c>next</c>: used in calendar UIDs, feed GUIDs and <c>/ahead/</c> anchors (ADR-0010).</summary>
    public static string ToToken(this KnownDateKind kind) => kind switch
    {
        KnownDateKind.Expiry => "expiry",
        KnownDateKind.Next => "next",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown known-date kind."),
    };

    /// <summary>Returns <c>upcoming</c>, <c>ended</c>, <c>recorded</c> or <c>passed_unrecorded</c> (ADR-0010).</summary>
    public static string ToToken(this KnownDateState state) => state switch
    {
        KnownDateState.Upcoming => "upcoming",
        KnownDateState.Ended => "ended",
        KnownDateState.Recorded => "recorded",
        KnownDateState.PassedUnrecorded => "passed_unrecorded",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown known-date state."),
    };
}
