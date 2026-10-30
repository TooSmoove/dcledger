using System.Text;
using DcLedger.Application;
using DcLedger.Domain;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using IcalCalendar = Ical.Net.Calendar;
using IcalEventStatus = Ical.Net.EventStatus;

namespace DcLedger.Publishing.Ics;

/// <summary>
/// Serializes events as one iCalendar object (SPEC AC3, AC4): DTSTART in America/New_York with the pinned VTIMEZONE,
/// the stable UID, SEQUENCE from the record and STATUS:CANCELLED for cancelled meetings. Every value comes from the
/// data, so the same events give the same bytes (CRLF, UTF-8 without a BOM, folded at 75 octets by Ical.Net).
/// </summary>
internal static class IcsCalendarWriter
{
    // Fixed instead of Ical.Net's default, which names the library version and would change the bytes on every bump.
    private const string ProductId = "-//dc-ledger//Hearing Radar//EN";

    // How often calendar apps that honor it (Outlook) should refresh: publish runs every 6 hours (#23), and
    // cancellations should reach subscribers that soon.
    private const string RefreshInterval = "PT6H";

    /// <summary>Returns the calendar named <paramref name="calendarName"/> holding <paramref name="events"/>, in their order.</summary>
    public static byte[] Write(string calendarName, IEnumerable<ExportEvent> events)
    {
        var calendar = new IcalCalendar { ProductId = ProductId };
        calendar.AddProperty("X-WR-CALNAME", calendarName);
        calendar.AddProperty("X-PUBLISHED-TTL", RefreshInterval);
        foreach (var item in events)
        {
            calendar.Events.Add(ToVEvent(item));
        }

        var text = new CalendarSerializer().SerializeToString(calendar)
            ?? throw new InvalidOperationException("Ical.Net serialized the calendar to nothing.");
        return Encoding.UTF8.GetBytes(PinnedTimeZone.InsertInto(text));
    }

    private static CalendarEvent ToVEvent(ExportEvent item)
    {
        var ledgerEvent = item.Event;
        var start = EasternTime.ToEastern(ledgerEvent.StartsAt);
        var cancelled = ledgerEvent.Status == EventStatus.Cancelled;
        var vevent = new CalendarEvent
        {
            Uid = ledgerEvent.Uid,
            // Without METHOD, DTSTAMP means "last revised" (RFC 5545 §3.8.7.2). The newest document retrieval stands in
            // for that, so an unchanged event keeps the same bytes on every publish.
            DtStamp = new CalDateTime(item.Documents.Max(document => document.RetrievedAtUtc).UtcDateTime),
            DtStart = new CalDateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, start.Second, PinnedTimeZone.Id),
            Summary = cancelled ? $"Cancelled: {ledgerEvent.Title}" : ledgerEvent.Title,
            Description = Describe(item),
            Sequence = ledgerEvent.Sequence,
        };
        if (cancelled)
        {
            vevent.Status = IcalEventStatus.Cancelled;
        }

        if (ledgerEvent.Location is { } location)
        {
            // GEO (RFC 5545 §3.8.1.6); Ical.Net writes it with 6 decimals, culture-invariant.
            vevent.GeographicLocation = new GeographicLocation(location.Latitude, location.Longitude);
        }

        return vevent;
    }

    /// <summary>Jurisdiction and body, the case number, then each source with its retrieval time and SHA-256 (SPEC AC2).</summary>
    private static string Describe(ExportEvent item)
    {
        var lines = new List<string> { $"{item.Jurisdiction.Name}, {item.Event.Body}" };
        if (item.Event.CaseNumber is { } caseNumber)
        {
            lines.Add($"Case: {caseNumber}");
        }

        lines.AddRange(item.Documents.Select(document =>
            $"Source: {document.Url.AbsoluteUri} (retrieved {IsoTime.Eastern(document.RetrievedAtUtc)}, SHA-256 {document.Id.Value})"));
        return string.Join('\n', lines);
    }
}
