using System.Globalization;
using System.Text;
using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>
/// Renders review issues and their follow-up comments (issue #18). Deterministic: the same candidate always gives the
/// same text, with <c>\n</c> line endings on every OS. Never shows a source title (the candidate doesn't carry one);
/// every source string passes through <see cref="IssueText"/>.
/// </summary>
public static class ReviewIssueRenderer
{
    private const string Utc = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The issue that asks a reviewer to look at a candidate.</summary>
    public static ReviewIssue Render(ReviewCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var cases = CaseNumbers(candidate);
        var caseSuffix = cases.Count == 0 ? string.Empty : $" (case {string.Join(", ", cases)})";
        var title = IssueText.Title(string.Create(Invariant, $"Review: {candidate.JurisdictionName}, {candidate.Body}, {candidate.Date:yyyy-MM-dd}{caseSuffix}"));

        var body = new StringBuilder();
        Line(body, $"<!-- dc-ledger:meeting {candidate.Meeting.Platform.ToToken()}/{candidate.Meeting.Client}/{candidate.Meeting.MeetingId} -->");
        Line(body, $"A keyword rule matched {Plural(candidate.Items.Count, "agenda item")} for **{IssueText.Inline(candidate.JurisdictionName)}, {IssueText.Inline(candidate.Body)}** on **{When(candidate)}**. Publish it only if it's about a data center.");
        Line(body);
        Line(body, "| | |");
        Line(body, "|---|---|");
        Line(body, $"| Meeting | {MeetingLink(candidate)} |");
        AgendaRows(body, candidate.Agenda, previous: null);
        Line(body, $"| First seen | {candidate.FirstSeenUtc.UtcDateTime.ToString(Utc, Invariant)} (the review clock starts here, SPEC AC9) |");
        Line(body);
        MatchedItems(body, candidate);
        Warnings(body, candidate);
        Line(body, "### To publish");
        Line(body);
        Line(body, "Add both files in one pull request. Write a neutral `title` (never the source's wording) and check `starts_at` against the agenda.");
        Line(body);
        DocumentFile(body, candidate.Agenda);
        Line(body);
        EventFile(body, candidate, cases);
        return new ReviewIssue(title, body.ToString());
    }

    /// <summary>The comment posted when a meeting's agenda changes after its issue was opened.</summary>
    public static string RenderAgendaChanged(ReviewCandidate candidate, Sha256Hash previous)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var body = new StringBuilder();
        Line(body, $"<!-- dc-ledger:agenda-changed {candidate.Agenda.Sha256.Value} -->");
        Line(body, $"The agenda changed: a new copy was retrieved {candidate.Agenda.RetrievedAtUtc.UtcDateTime.ToString(Utc, Invariant)}.");
        Line(body);
        Line(body, "| | |");
        Line(body, "|---|---|");
        AgendaRows(body, candidate.Agenda, previous);
        Line(body);
        MatchedItems(body, candidate);
        Line(body, "If this meeting is already published, add the new document row and list its ID in the event's `document_ids`.");
        Line(body);
        DocumentFile(body, candidate.Agenda);
        return body.ToString();
    }

    /// <summary>A suggested event ID: jurisdiction, body and date as lowercase words joined by single hyphens.</summary>
    internal static string EventSlug(string jurisdictionId, string body, DateOnly date)
    {
        var slug = new StringBuilder();
        foreach (var c in string.Create(Invariant, $"{jurisdictionId} {body} {date:yyyy-MM-dd}"))
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(char.ToLowerInvariant(c));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString().TrimEnd('-');
    }

    private static void AgendaRows(StringBuilder body, AgendaDocument agenda, Sha256Hash? previous)
    {
        Line(body, $"| Agenda | [agenda file](<{agenda.Uri.AbsoluteUri}>) · {IssueText.Inline(agenda.MediaType)} · {agenda.Bytes.ToString("N0", Invariant)} bytes |");
        var was = previous is { } old ? $" (was `{old.Value}`)" : string.Empty;
        Line(body, $"| SHA-256 | `{agenda.Sha256.Value}`{was} |");
        Line(body, $"| Retrieved | {agenda.RetrievedAtUtc.UtcDateTime.ToString(Utc, Invariant)} |");
    }

    private static void MatchedItems(StringBuilder body, ReviewCandidate candidate)
    {
        Line(body, "### Matched items");
        Line(body);
        if (candidate.Items.Count == 0)
        {
            Line(body, "No agenda item matches the keyword rules any more. Check the agenda before publishing.");
            Line(body);
            return;
        }

        Line(body, "Original titles are private and never copied here; read them in the agenda.");
        Line(body);
        Line(body, "| Agenda no. | Case | Type | Rules |");
        Line(body, "|---|---|---|---|");
        foreach (var item in candidate.Items)
        {
            var caseNumber = IssueText.CaseNumber(item.CaseNumber) ?? "—";
            Line(body, $"| {IssueText.Inline(item.AgendaNumber)} | {caseNumber} | {IssueText.Inline(item.MatterType)} | {IssueText.Inline(string.Join(", ", item.Rules))} |");
        }

        Line(body);
    }

    private static void Warnings(StringBuilder body, ReviewCandidate candidate)
    {
        if (candidate.SameSlotMeetingIds.Count > 0)
        {
            var ids = string.Join(", ", candidate.SameSlotMeetingIds.Select(IssueText.Inline));
            Warning(body, $"{PlatformName(candidate)} also lists {(candidate.SameSlotMeetingIds.Count == 1 ? "meeting" : "meetings")} {ids} for the same body, date and time, and its public site can hide some of them. Open each meeting page and approve only the one that's live.");
        }

        if (candidate.PossiblyCancelled)
        {
            Warning(body, "A comment on this meeting mentions a cancellation. Confirm it on the meeting page before publishing.");
        }

        if (candidate.StartsAt is null)
        {
            Warning(body, candidate.Time is { } time
                ? $"{time.ToString("h:mm tt", Invariant)} on this date isn't a valid Eastern time (daylight saving). Take the start from the agenda."
                : "The source gives no start time. Take it from the agenda.");
        }
    }

    private static void DocumentFile(StringBuilder body, AgendaDocument agenda)
    {
        Line(body, $"`data/documents/{agenda.Sha256.Value}.yaml`");
        Line(body);
        Line(body, "```yaml");
        Line(body, $"id: {agenda.Sha256.Value}");
        Line(body, $"url: {IssueText.YamlQuoted(agenda.Uri.AbsoluteUri)}");
        Line(body, $"retrieved_at: {agenda.RetrievedAtUtc.UtcDateTime.ToString(Utc, Invariant)}");
        Line(body, $"media_type: {IssueText.YamlQuoted(agenda.MediaType)}");
        Line(body, $"bytes: {agenda.Bytes.ToString(Invariant)}");
        Line(body, "source_type: official");
        Line(body, "```");
    }

    private static void EventFile(StringBuilder body, ReviewCandidate candidate, IReadOnlyList<string> cases)
    {
        var slug = EventSlug(candidate.JurisdictionId, candidate.Body, candidate.Date);
        Line(body, $"`data/events/{slug}.yaml`");
        Line(body);
        Line(body, "```yaml");
        Line(body, $"id: {slug}");
        Line(body, $"jurisdiction_id: {candidate.JurisdictionId}");
        Line(body, $"platform: {candidate.Meeting.Platform.ToToken()}");
        Line(body, $"platform_meeting_id: {IssueText.YamlQuoted(candidate.Meeting.MeetingId)}");
        Line(body, $"body: {IssueText.YamlQuoted(candidate.Body)}");
        Line(body, "title: \"\" # REQUIRED: a neutral title in your own words");
        if (cases.Count > 0)
        {
            var others = cases.Count > 1 ? $" # also on this agenda: {string.Join(", ", cases.Skip(1))}" : string.Empty;
            Line(body, $"case_number: {IssueText.YamlQuoted(cases[0])}{others}");
        }

        Line(body, candidate.StartsAt is { } startsAt
            ? $"starts_at: {startsAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", Invariant)}"
            : "starts_at: \"\" # REQUIRED: from the agenda, with the Eastern offset, for example 2026-11-05T17:30:00-05:00");
        Line(body, "status: scheduled");
        Line(body, "sequence: 0");
        Line(body, "label: confirmed_official");
        Line(body, "document_ids:");
        Line(body, $"  - {candidate.Agenda.Sha256.Value}");
        Line(body, $"first_seen_utc: {candidate.FirstSeenUtc.UtcDateTime.ToString(Utc, Invariant)}");
        Line(body, "```");
    }

    private static IReadOnlyList<string> CaseNumbers(ReviewCandidate candidate) =>
        [.. candidate.Items.Select(item => IssueText.CaseNumber(item.CaseNumber)).OfType<string>().Distinct(StringComparer.Ordinal)];

    private static string When(ReviewCandidate candidate)
    {
        var date = candidate.Date.ToString("dddd, MMMM d, yyyy", Invariant);
        return (candidate.Time, candidate.StartsAt) switch
        {
            ({ } time, not null) => $"{date} at {time.ToString("h:mm tt", Invariant)} Eastern",
            ({ } time, null) => $"{date} at {time.ToString("h:mm tt", Invariant)} (not a valid Eastern time)",
            _ => $"{date} (no start time given)",
        };
    }

    private static string MeetingLink(ReviewCandidate candidate)
    {
        var label = $"{PlatformName(candidate)} meeting {IssueText.Inline(candidate.Meeting.MeetingId)}";
        return candidate.MeetingPageUri is { } page ? $"[{label}](<{page.AbsoluteUri}>)" : label;
    }

    private static string PlatformName(ReviewCandidate candidate) => candidate.Meeting.Platform switch
    {
        Platform.Legistar => "Legistar",
        _ => "The platform",
    };

    private static string Plural(int count, string noun) =>
        string.Create(Invariant, $"{count} {noun}{(count == 1 ? string.Empty : "s")}");

    private static void Warning(StringBuilder body, string text)
    {
        Line(body, "> [!WARNING]");
        Line(body, $"> {text}");
        Line(body);
    }

    private static void Line(StringBuilder body, string text = "") => body.Append(text).Append('\n');
}
