using System.Text;
using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Publishing;

/// <summary>
/// What a known date's calendar entry and feed items say (ADR-0010 Decisions 4 and 5): the decision that states it,
/// the date in Eastern words, the stated terms, the state when it matters, the label, every source with its retrieval
/// time and SHA-256 (SPEC AC2), links to the decision and to its <c>/ahead/</c> row, and the coverage line. Only fixed
/// words and values copied from the record: nothing about what will happen.
/// </summary>
internal static class KnownDateDescription
{
    /// <summary>Plain text, one fact per line, for a calendar DESCRIPTION.</summary>
    public static string Text(Uri siteUrl, ExportKnownDate item)
    {
        var lines = Facts(item).ToList();
        lines.AddRange(item.Action.Documents.Select(document =>
            $"Source: {document.Url.AbsoluteUri} (retrieved {IsoTime.Eastern(document.RetrievedAtUtc)}, SHA-256 {document.Id.Value}){RetrievalNote.After(document)}"));
        lines.Add($"Decision: {DecisionUrl(siteUrl, item.Action.Action)}");
        lines.Add($"Coming up on the record: {AheadUrl(siteUrl, item.Date)}");
        lines.Add(RecordText.KnownDateCoverage);
        return string.Join('\n', lines);
    }

    /// <summary>Escaped HTML, which RSS readers render, for a feed item's description.</summary>
    public static string Html(Uri siteUrl, ExportKnownDate item)
    {
        var html = new StringBuilder();
        foreach (var fact in Facts(item))
        {
            html.Append("<p>").Append(RecordText.Html(fact)).Append("</p>");
        }

        html.Append("<ul>");
        foreach (var document in item.Action.Documents)
        {
            var url = RecordText.Html(document.Url.AbsoluteUri);
            html.Append("<li><a href=\"").Append(url).Append("\">").Append(url).Append("</a> (retrieved ")
                .Append(IsoTime.Eastern(document.RetrievedAtUtc)).Append(", SHA-256 ").Append(document.Id.Value).Append(')')
                .Append(RecordText.Html(RetrievalNote.After(document))).Append("</li>");
        }

        html.Append("</ul>");
        var decision = RecordText.Html(DecisionUrl(siteUrl, item.Action.Action));
        var ahead = RecordText.Html(AheadUrl(siteUrl, item.Date));
        html.Append("<p><a href=\"").Append(decision).Append("\">The decision</a> · <a href=\"").Append(ahead)
            .Append("\">Coming up on the record</a></p>");
        html.Append("<p>").Append(RecordText.Html(RecordText.KnownDateCoverage)).Append("</p>");
        return html.ToString();
    }

    /// <summary>The decision's page: its meeting's, or its jurisdiction's when the meeting isn't in the ledger (as in the feed).</summary>
    public static string DecisionUrl(Uri siteUrl, LocalAction action) =>
        new Uri(siteUrl, action.EventId is { } eventId ? $"items/{eventId}/" : $"jurisdictions/{action.JurisdictionId}/").AbsoluteUri;

    /// <summary>The date's row on <c>/ahead/</c>.</summary>
    public static string AheadUrl(Uri siteUrl, KnownDate date) => new Uri(siteUrl, $"ahead/#{date.Anchor}").AbsoluteUri;

    private static IEnumerable<string> Facts(ExportKnownDate item)
    {
        var action = item.Action.Action;
        yield return $"{item.Action.Jurisdiction.Name}: {RecordText.ActionTypeText(action.ActionType)} {RecordText.OutcomeText(action.Outcome)}, decided {RecordText.LongDate(action.DecidedOn)}.";
        yield return item.Date.Kind switch
        {
            KnownDateKind.Expiry => $"Expiry date: {RecordText.LongDate(item.Date.Date)}.",
            _ => $"Date set for the next step: {RecordText.LongDate(item.Date.Date)}.",
        };
        if (item.State == KnownDateState.PassedUnrecorded)
        {
            yield return RecordText.PassedUnrecorded;
        }

        if (action.Terms.Status is { } status)
        {
            yield return $"Status: {RecordText.StatusText(status)}";
        }

        if (action.Terms.Threshold is { } threshold)
        {
            yield return $"Threshold: {RecordText.Quoted(threshold)}";
        }

        if (action.Terms.Scope is { } scope)
        {
            yield return $"Covers: {RecordText.Quoted(scope)}";
        }

        yield return RecordText.LabelText(action.Label);
    }
}
