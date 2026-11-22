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
/// Serializes known dates as one iCalendar object (ADR-0010 Decision 4). Each is an all-day VEVENT (DTSTART;VALUE=DATE,
/// DTEND the next day), so the file needs no VTIMEZONE. The UID leaves the date out, so a corrected or rescheduled date
/// updates the same entry; SEQUENCE is the action's. An <see cref="KnownDateState.Ended"/> expiry is
/// STATUS:CANCELLED. There is no VALARM: subscribers set their own alerts, and the feed gives advance notice. Every
/// value comes from the data, so the same ledger and D give the same bytes.
/// </summary>
internal static class AheadCalendarWriter
{
    // Fixed instead of Ical.Net's default, which names the library version and would change the bytes on every bump.
    private const string ProductId = "-//dc-ledger//Coming up on the record//EN";

    // Publish runs every 6 hours, like the radar calendars.
    private const string RefreshInterval = "PT6H";

    /// <summary>Returns the calendar named <paramref name="calendarName"/> holding <paramref name="dates"/>, in their order.</summary>
    public static byte[] Write(Uri siteUrl, string calendarName, IEnumerable<ExportKnownDate> dates)
    {
        var calendar = new IcalCalendar { ProductId = ProductId };
        calendar.AddProperty("X-WR-CALNAME", calendarName);
        calendar.AddProperty("X-PUBLISHED-TTL", RefreshInterval);
        foreach (var item in dates)
        {
            calendar.Events.Add(ToVEvent(siteUrl, item));
        }

        var text = new CalendarSerializer().SerializeToString(calendar)
            ?? throw new InvalidOperationException("Ical.Net serialized the calendar to nothing.");

        // A calendar needs at least one component (RFC 5545 §3.6). With no dates yet, the pinned VTIMEZONE is that
        // component, as in an empty radar calendar; with dates it is left out, since all-day dates need no zone.
        return Encoding.UTF8.GetBytes(calendar.Events.Count == 0 ? PinnedTimeZone.InsertInto(text) : text);
    }

    private static CalendarEvent ToVEvent(Uri siteUrl, ExportKnownDate item)
    {
        var date = item.Date.Date;
        var next = date.AddDays(1);
        var action = item.Action.Action;
        var vevent = new CalendarEvent
        {
            Uid = item.Date.Uid,
            // As for the radar: without METHOD, DTSTAMP means "last revised"; the action's newest document retrieval
            // stands in for that, so an unchanged date keeps the same bytes on every publish.
            DtStamp = new CalDateTime(item.Action.Documents.Max(document => document.RetrievedAtUtc).UtcDateTime),
            DtStart = new CalDateTime(date.Year, date.Month, date.Day),
            DtEnd = new CalDateTime(next.Year, next.Month, next.Day),
            Summary = RecordText.KnownDateName(item.Date.Kind, action.ActionType, item.Action.Jurisdiction.Name),
            Description = KnownDateDescription.Text(siteUrl, item),
            Sequence = action.Sequence,
        };
        if (item.State == KnownDateState.Ended)
        {
            vevent.Status = IcalEventStatus.Cancelled;
        }

        return vevent;
    }
}
