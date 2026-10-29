using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Adapters.Legistar;

/// <summary>
/// Reads meetings and agenda items from the Legistar Web API (open, no token; spike S1). One request for the events
/// in the window, then one per meeting of a configured body for its items, all through <see cref="IFetcher"/>. Agendas
/// are downloaded on request through the meeting's public page (<see cref="GetAgendaAsync"/>).
/// </summary>
public sealed class LegistarAdapter(IFetcher fetcher) : ISourceAdapter
{
    /// <summary>Legistar returns at most 1,000 rows per request; a full page could mean meetings were cut off.</summary>
    internal const int PageLimit = 1000;

    private static readonly Uri ApiBase = new("https://webapi.legistar.com/v1/");

    /// <inheritdoc />
    public Platform Platform => Platform.Legistar;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SourceMeeting>> GetMeetingsAsync(Jurisdiction jurisdiction, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jurisdiction);
        var client = jurisdiction.LegistarClient;
        if (jurisdiction.Platform != Platform.Legistar || string.IsNullOrWhiteSpace(client))
        {
            throw new ArgumentException($"{jurisdiction.Id} isn't a Legistar jurisdiction.", nameof(jurisdiction));
        }

        if (endDate <= startDate)
        {
            throw new ArgumentOutOfRangeException(nameof(endDate), endDate, "The window must end after it starts.");
        }

        var eventsUri = EventsUri(client, startDate, endDate);
        var events = await ReadAsync(eventsUri, LegistarJsonContext.Default.LegistarEventArray, cancellationToken).ConfigureAwait(false);
        if (events.Length >= PageLimit)
        {
            throw LegistarMapping.FormatChanged(eventsUri, $"returned {events.Length} meetings, Legistar's page limit, so some may be missing.");
        }

        var bodies = jurisdiction.Bodies.Select(body => body.Trim()).ToHashSet(StringComparer.Ordinal);
        var meetings = new List<(int EventId, SourceMeeting Meeting)>();
        foreach (var source in events)
        {
            var date = DateOnly.FromDateTime(source.EventDate);
            if (date < startDate || date >= endDate || !bodies.Contains(source.EventBodyName.Trim()))
            {
                continue;
            }

            var meeting = LegistarMapping.ToMeeting(client, source, eventsUri);
            var items = await ReadAsync(ItemsUri(client, source.EventId), LegistarJsonContext.Default.LegistarEventItemArray, cancellationToken).ConfigureAwait(false);
            meetings.Add((source.EventId, meeting with { Items = LegistarMapping.ToItems(items) }));
        }

        return [.. meetings.OrderBy(m => m.Meeting.Date).ThenBy(m => m.Meeting.Time).ThenBy(m => m.EventId).Select(m => m.Meeting)];
    }

    /// <summary>
    /// Downloads the agenda through the meeting's public page on <c>{client}.legistar.com</c>, which links it as
    /// <c>View.ashx?M=A&amp;ID=…&amp;GUID=…</c>. The API's <c>EventAgendaFile</c> points to Legistar's file host, whose
    /// robots.txt disallows every crawler (checked Oct 29, 2026; issue #19); the meeting page's host allows it and serves
    /// the same bytes. Throws <see cref="SourceFailedException"/>: FormatChanged when there is no meeting page on that host,
    /// the page doesn't link exactly one agenda, or the link doesn't answer with a PDF.
    /// </summary>
    public async Task<FetchedResource> GetAgendaAsync(SourceMeeting meeting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(meeting);
        var page = meeting.MeetingPageUri;
        var site = $"{meeting.Key.Client}.legistar.com";
        if (page is null || page.Scheme != Uri.UriSchemeHttps || !page.Host.Equals(site, StringComparison.OrdinalIgnoreCase))
        {
            throw LegistarMapping.FormatChanged(page ?? ApiBase, $"meeting {meeting.Key.MeetingId} has no meeting page on {site} to find its agenda on.");
        }

        var html = await GetAsync(page, cancellationToken).ConfigureAwait(false);
        if (html.MediaType is { } pageType && !pageType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            throw LegistarMapping.FormatChanged(page, $"answered {pageType}, not HTML.");
        }

        var agendaUri = LegistarMeetingPage.AgendaLink(page, html.Content);
        var agenda = await GetAsync(agendaUri, cancellationToken).ConfigureAwait(false);
        if (!agenda.Content.Span.StartsWith("%PDF-"u8))
        {
            throw LegistarMapping.FormatChanged(agenda.FinalUri, "didn't answer with a PDF.");
        }

        return agenda;
    }

    /// <summary><c>/v1/{client}/events?$filter=EventDate ge datetime'{start}' and EventDate lt datetime'{end}'</c>.</summary>
    internal static Uri EventsUri(string client, DateOnly startDate, DateOnly endDate)
    {
        var filter = string.Create(CultureInfo.InvariantCulture, $"EventDate ge datetime'{startDate:yyyy-MM-dd}' and EventDate lt datetime'{endDate:yyyy-MM-dd}'");
        return new Uri(ApiBase, $"{Uri.EscapeDataString(client)}/events?$filter={Uri.EscapeDataString(filter)}");
    }

    /// <summary><c>/v1/{client}/events/{eventId}/eventitems</c>.</summary>
    internal static Uri ItemsUri(string client, int eventId) =>
        new(ApiBase, string.Create(CultureInfo.InvariantCulture, $"{Uri.EscapeDataString(client)}/events/{eventId}/eventitems"));

    private async Task<T[]> ReadAsync<T>(Uri uri, JsonTypeInfo<T[]> type, CancellationToken cancellationToken)
        where T : class
    {
        var resource = await GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (resource.MediaType is { } mediaType && !IsJson(mediaType))
        {
            throw LegistarMapping.FormatChanged(uri, $"answered {mediaType}, not JSON.");
        }

        T[]? values;
        try
        {
            values = JsonSerializer.Deserialize(resource.Content.Span, type);
        }
        catch (JsonException exception)
        {
            throw LegistarMapping.FormatChanged(uri, exception.Message, exception);
        }

        if (values is null || Array.Exists(values, value => value is null))
        {
            throw LegistarMapping.FormatChanged(uri, "expected a JSON array of objects.");
        }

        return values;
    }

    private async Task<FetchedResource> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            return await fetcher.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (FetchFailedException exception)
        {
            throw Translate(exception);
        }
    }

    private static bool IsJson(string mediaType) =>
        mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("text/json", StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);

    /// <summary>Fetch failures as ADR-0005 source failures: refusals need a human, outages wait for the next run.</summary>
    private static SourceFailedException Translate(FetchFailedException exception)
    {
        var failure = exception.Failure switch
        {
            FetchFailure.RobotsDisallowed => SourceFailure.Rejected,
            FetchFailure.HttpStatus when exception.StatusCode is 408 or 429 or >= 500 => SourceFailure.Unavailable,
            FetchFailure.HttpStatus => SourceFailure.Rejected,
            FetchFailure.TooLarge => SourceFailure.FormatChanged,
            _ => SourceFailure.Unavailable,
        };
        return new SourceFailedException(failure, exception.Uri, $"Legistar: {exception.Message}", exception);
    }
}
