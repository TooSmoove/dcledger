using System.Globalization;
using DcLedger.Domain;

namespace DcLedger.Publishing;

/// <summary>
/// The fixed words the published files use for a record's values, mirroring the site's (site/src/lib/radar.js). One
/// table here, so the feed and the calendars can't drift apart.
/// </summary>
internal static class RecordText
{
    /// <summary>Text copied from a document as written, in curly quotes.</summary>
    public static string Quoted(string text) => $"\u201C{text}\u201D";

    /// <summary>A date in words, such as "Sunday, February 14, 2027".</summary>
    public static string LongDate(DateOnly date) => date.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>The site's wording for a decision's status (site/src/lib/radar.js).</summary>
    public static string StatusText(ActionStatus status) => status switch
    {
        ActionStatus.InEffect => "In effect",
        ActionStatus.Expired => "Expired",
        ActionStatus.Superseded => "Replaced or extended by a later decision",
        ActionStatus.Repealed => "Repealed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown action status."),
    };

    /// <summary>The site's wording for action types (site/src/lib/radar.js), capitalized to start a title.</summary>
    public static string ActionTypeText(ActionType actionType) => actionType switch
    {
        ActionType.Moratorium => "Moratorium",
        ActionType.Rezoning => "Rezoning",
        ActionType.TextAmendment => "Ordinance text amendment",
        ActionType.Resolution => "Resolution",
        _ => throw new ArgumentOutOfRangeException(nameof(actionType), actionType, "Unknown action type."),
    };

    /// <summary>The site's wording for outcomes, lowercase to follow the action type.</summary>
    public static string OutcomeText(ActionOutcome outcome) => outcome switch
    {
        ActionOutcome.Adopted => "adopted",
        ActionOutcome.AdoptedAmended => "adopted with amendments",
        ActionOutcome.Failed => "failed",
        ActionOutcome.Continued => "continued to a later meeting",
        ActionOutcome.Tabled => "tabled",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown outcome."),
    };

    /// <summary>The site's wording for the sourcing label.</summary>
    public static string LabelText(EvidenceLabel label) => label switch
    {
        EvidenceLabel.ReportedNews => "Reported (news)",
        EvidenceLabel.ConfirmedOfficial => "Confirmed (official record)",
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown label."),
    };

    /// <summary>
    /// The fixed name of a known date (ADR-0010 Decision 4), used as the calendar SUMMARY and in feed titles: "Moratorium
    /// expiry date: Greensboro" or "Date set for the next step: Davidson County, moratorium". No editorial words, and
    /// never "deadline": the record states a date, not what will happen on it.
    /// </summary>
    public static string KnownDateName(KnownDateKind kind, ActionType actionType, string jurisdictionName) => kind switch
    {
        KnownDateKind.Expiry => $"{ActionTypeText(actionType)} expiry date: {jurisdictionName}",
        KnownDateKind.Next => $"Date set for the next step: {jurisdictionName}, {ActionTypeText(actionType).ToLowerInvariant()}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown known-date kind."),
    };

    /// <summary>The flag for a known date that has passed with nothing recorded (ADR-0010 Decision 3, docs/wording-bank.md).</summary>
    public const string PassedUnrecorded = "Date passed. Nothing recorded for it yet.";

    /// <summary>The line every known date carries: it is a date the record states, not a prediction (ADR-0010).</summary>
    public const string KnownDateCoverage = "Only dates stated in an archived document are listed. A body can act on a matter before or after these dates.";

    /// <summary>The four characters HTML text and attributes need escaped; the XML writer then escapes the markup itself.</summary>
    public static string Html(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
