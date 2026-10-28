using System.Globalization;
using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Adapters.Legistar;

/// <summary>Legistar records to source meetings. Anything that doesn't fit the expected format fails loudly.</summary>
internal static class LegistarMapping
{
    /// <summary>How Legistar writes <c>EventTime</c>, for example <c>5:30 PM</c> (recorded fixtures).</summary>
    private const string TimeFormat = "h:mm tt";

    /// <summary>The meeting without its items; checked before any item request is made.</summary>
    public static SourceMeeting ToMeeting(string client, LegistarEvent source, Uri origin)
    {
        var date = DateOnly.FromDateTime(source.EventDate);
        var time = ParseTime(source, origin);
        DateTimeOffset? startsAt = null;
        if (time is { } wallTime && EasternTime.TryToInstant(date.ToDateTime(wallTime), out var instant, out _))
        {
            startsAt = instant;
        }

        return new SourceMeeting
        {
            Key = new MeetingKey(Platform.Legistar, client, source.EventId.ToString(CultureInfo.InvariantCulture)),
            Body = source.EventBodyName.Trim(),
            Date = date,
            Time = time,
            StartsAt = startsAt,
            AgendaUri = ParseUri(source.EventAgendaFile, "EventAgendaFile", source, origin),
            MeetingPageUri = ParseUri(source.EventInSiteUrl, "EventInSiteURL", source, origin),
            PossiblyCancelled = source.EventComment?.Contains("cancel", StringComparison.OrdinalIgnoreCase) == true,
        };
    }

    /// <summary>Items in agenda order (sequence, then ID).</summary>
    public static IReadOnlyList<SourceItem> ToItems(LegistarEventItem[] items) =>
        [.. items.OrderBy(item => item.EventItemAgendaSequence ?? int.MaxValue).ThenBy(item => item.EventItemId).Select(ToItem)];

    public static SourceFailedException FormatChanged(Uri uri, string reason, Exception? inner = null) =>
        new(SourceFailure.FormatChanged, uri, $"Legistar format changed? {uri.AbsoluteUri} {reason}", inner);

    private static SourceItem ToItem(LegistarEventItem item) => new()
    {
        ItemId = item.EventItemId.ToString(CultureInfo.InvariantCulture),
        AgendaNumber = Blank(item.EventItemAgendaNumber),
        Title = item.EventItemTitle?.Trim() ?? string.Empty,
        MatterName = Blank(item.EventItemMatterName),
        CaseNumber = Blank(item.EventItemMatterFile),
        MatterType = Blank(item.EventItemMatterType),
    };

    private static TimeOnly? ParseTime(LegistarEvent source, Uri origin)
    {
        var text = source.EventTime?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw FormatChanged(origin, $"meeting {source.EventId} has EventTime '{text}', not like '5:30 PM'.");
    }

    private static Uri? ParseUri(string? text, string field, LegistarEvent source, Uri origin)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : throw FormatChanged(origin, $"meeting {source.EventId} has a {field} that isn't an http(s) URL.");
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
