using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Adapters.Legistar;

/// <summary>
/// Reads meetings and agenda items from the Legistar Web API (open, no token; spike S1). One request for the events
/// in the window, then one per meeting of a configured body for its items, all through <see cref="IFetcher"/>.
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
        FetchedResource resource;
        try
        {
            resource = await fetcher.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (FetchFailedException exception)
        {
            throw Translate(exception);
        }

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
