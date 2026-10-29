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
}
