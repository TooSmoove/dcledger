using System.Globalization;
using System.Text;
using System.Xml;
using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Publishing.Feed;

/// <summary>
/// The RSS 2.0 feed (SPEC AC5). Export keeps no history, so each event contributes one item for its current revision:
/// the GUID is the calendar UID plus <c>sequence</c>, which every published change raises (<c>SEQUENCE_NOT_RAISED</c>).
/// A change therefore arrives in readers as a new item, and an unchanged event keeps its GUID. A cancellation is such a
/// change (SPEC AC4). Written with <see cref="XmlWriter"/> rather than a syndication library so the bytes depend only on
/// the ledger: every date comes from it, including <c>lastBuildDate</c>.
/// </summary>
internal static class RssFeedWriter
{
    private const string AtomNamespace = "http://www.w3.org/2005/Atom";

    private const string Title = "NC Data Center Ledger: Hearing Radar";

    private const string Description =
        "Reviewed North Carolina local-government meetings about data centers, each linked to its official source. "
        + "A new item appears for each new meeting and each published change, including cancellations.";

    // Minutes; publish runs every 6 hours (#23), like the calendars' refresh interval.
    private const string Ttl = "360";

    public static byte[] Write(Uri siteUrl, IEnumerable<ExportEvent> events)
    {
        var items = events
            .Select(item => (Item: item, Published: item.Documents.Max(document => document.RetrievedAtUtc)))
            .OrderByDescending(entry => entry.Published)
            .ThenBy(entry => entry.Item.Event.Id, StringComparer.Ordinal)
            .ToList();

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };
        using var stream = new MemoryStream();
        using (var xml = XmlWriter.Create(stream, settings))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("rss");
            xml.WriteAttributeString("version", "2.0");
            xml.WriteAttributeString("xmlns", "atom", null, AtomNamespace);
            xml.WriteStartElement("channel");
            xml.WriteElementString("title", Title);
            xml.WriteElementString("link", Absolute(siteUrl, "radar/"));
            xml.WriteElementString("description", Description);
            xml.WriteElementString("language", "en-us");
            if (items.Count > 0)
            {
                xml.WriteElementString("lastBuildDate", Rfc822(items[0].Published));
            }

            xml.WriteElementString("ttl", Ttl);
            xml.WriteStartElement("atom", "link", AtomNamespace);
            xml.WriteAttributeString("href", Absolute(siteUrl, "feed.xml"));
            xml.WriteAttributeString("rel", "self");
            xml.WriteAttributeString("type", "application/rss+xml");
            xml.WriteEndElement();
            foreach (var (item, published) in items)
            {
                WriteItem(xml, siteUrl, item, published);
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteItem(XmlWriter xml, Uri siteUrl, ExportEvent item, DateTimeOffset published)
    {
        var ledgerEvent = item.Event;
        xml.WriteStartElement("item");
        xml.WriteElementString("title", $"{Change(ledgerEvent)}{ledgerEvent.Title} ({item.Jurisdiction.Name})");
        xml.WriteElementString("link", Absolute(siteUrl, $"items/{ledgerEvent.Id}/"));
        xml.WriteStartElement("guid");
        xml.WriteAttributeString("isPermaLink", "false");
        xml.WriteString($"{ledgerEvent.Uid}/{ledgerEvent.Sequence.ToString(CultureInfo.InvariantCulture)}");
        xml.WriteEndElement();
        xml.WriteElementString("pubDate", Rfc822(published));
        xml.WriteElementString("description", Describe(item));
        xml.WriteEndElement();
    }

    /// <summary>What the revision is, as a title prefix: nothing for a meeting's first version.</summary>
    private static string Change(LedgerEvent ledgerEvent) =>
        ledgerEvent.Status == EventStatus.Cancelled ? "Cancelled: "
        : ledgerEvent.Sequence > 0 ? "Updated: "
        : string.Empty;

    /// <summary>The item body as escaped HTML, which RSS readers render: when, where, label and every source (SPEC AC2).</summary>
    private static string Describe(ExportEvent item)
    {
        var ledgerEvent = item.Event;
        var start = EasternTime.ToEastern(ledgerEvent.StartsAt).ToString("dddd, MMMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture);
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"<p>{Html(item.Jurisdiction.Name)}, {Html(ledgerEvent.Body)}: {start} Eastern.</p>");
        if (ledgerEvent.Status == EventStatus.Cancelled)
        {
            html.Append("<p><strong>Cancelled.</strong></p>");
        }

        if (ledgerEvent.CaseNumber is { } caseNumber)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p>Case: {Html(caseNumber)}</p>");
        }

        html.Append(CultureInfo.InvariantCulture, $"<p>{LabelText(ledgerEvent.Label)}</p><ul>");
        foreach (var document in item.Documents)
        {
            var url = Html(document.Url.AbsoluteUri);
            html.Append(CultureInfo.InvariantCulture,
                $"<li><a href=\"{url}\">{url}</a> (retrieved {IsoTime.Eastern(document.RetrievedAtUtc)}, SHA-256 {document.Id.Value})</li>");
        }

        html.Append("</ul>");
        return html.ToString();
    }

    private static string LabelText(EvidenceLabel label) => label switch
    {
        EvidenceLabel.ReportedNews => "Reported (news)",
        EvidenceLabel.ConfirmedOfficial => "Confirmed (official record)",
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown label."),
    };

    /// <summary>The four characters HTML text and attributes need escaped; the XML writer then escapes the markup itself.</summary>
    private static string Html(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>RFC 822 dates, as RSS 2.0 requires, always in UTC.</summary>
    private static string Rfc822(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("ddd, dd MMM yyyy HH:mm:ss '+0000'", CultureInfo.InvariantCulture);

    private static string Absolute(Uri siteUrl, string path) => new Uri(siteUrl, path).AbsoluteUri;
}
