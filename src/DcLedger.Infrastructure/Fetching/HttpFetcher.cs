using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using DcLedger.Application;
using DcLedger.Domain;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace DcLedger.Infrastructure.Fetching;

/// <summary>
/// <see cref="IFetcher"/> over HttpClient. Retries, timeouts and the circuit breaker come from the standard resilience
/// handler; this class adds robots.txt (per hop), redirect handling, the body timeout and the size cap. robots.txt is
/// cached for the life of the instance, so resolve one fetcher per run.
/// </summary>
internal sealed class HttpFetcher(HttpClient client, FetcherOptions options, TimeProvider timeProvider) : IFetcher
{
    private readonly ConcurrentDictionary<string, Lazy<Task<RobotsTxt>>> _robots = new(StringComparer.Ordinal);

    public async Task<FetchedResource> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!IsHttp(uri))
        {
            throw new ArgumentException($"Only absolute http and https URLs are fetched, not {uri}.", nameof(uri));
        }

        var current = uri;
        for (var hop = 0; ; hop++)
        {
            var robots = await RobotsForAsync(current, cancellationToken).ConfigureAwait(false);
            if (!robots.IsAllowed(current))
            {
                throw new FetchFailedException(current, FetchFailure.RobotsDisallowed, $"robots.txt disallows {current} for {options.RobotsToken}.");
            }

            using var response = await SendAsync(current, cancellationToken).ConfigureAwait(false);
            if (IsRedirect(response.StatusCode))
            {
                current = NextHop(uri, current, response, hop);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                throw new FetchFailedException(current, FetchFailure.HttpStatus,
                    string.Create(CultureInfo.InvariantCulture, $"{current} answered HTTP {status}."), status);
            }

            var content = await ReadBodyAsync(current, response, options.MaxBytes, truncate: false, cancellationToken).ConfigureAwait(false);
            return new FetchedResource(
                content,
                response.Content.Headers.ContentType?.MediaType,
                current,
                timeProvider.GetUtcNow(),
                Sha256Hash.Compute(content.Span));
        }
    }

    private async Task<RobotsTxt> RobotsForAsync(Uri uri, CancellationToken cancellationToken)
    {
        var site = uri.GetLeftPart(UriPartial.Authority);
        var entry = _robots.GetOrAdd(site, key => new Lazy<Task<RobotsTxt>>(() => LoadRobotsAsync(new Uri(key + "/robots.txt"), cancellationToken)));
        try
        {
            return await entry.Value.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _robots.TryRemove(new KeyValuePair<string, Lazy<Task<RobotsTxt>>>(site, entry)); // don't cache a cancelled load
            throw;
        }
    }

    /// <summary>RFC 9309 §2.3.1: 2xx parse, 4xx allow all, 5xx or unreachable disallow all. Up to five redirects.</summary>
    private async Task<RobotsTxt> LoadRobotsAsync(Uri robotsUri, CancellationToken cancellationToken)
    {
        var current = robotsUri;
        try
        {
            for (var hop = 0; ; hop++)
            {
                using var response = await SendAsync(current, cancellationToken).ConfigureAwait(false);
                if (IsRedirect(response.StatusCode))
                {
                    current = NextHop(robotsUri, current, response, hop);
                    continue;
                }

                var status = (int)response.StatusCode;
                if (status is >= 200 and < 300)
                {
                    var bytes = await ReadBodyAsync(current, response, options.MaxRobotsBytes, truncate: true, cancellationToken).ConfigureAwait(false);
                    return RobotsTxt.Parse(Encoding.UTF8.GetString(bytes.Span), options.RobotsToken);
                }

                return status is >= 400 and < 500 && status != 429 ? RobotsTxt.AllowAll : RobotsTxt.DisallowAll;
            }
        }
        catch (FetchFailedException)
        {
            return RobotsTxt.DisallowAll;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(options.UserAgent);
        request.Headers.Accept.ParseAdd("*/*"); // what curl sends; spike S1 got JSON from Legistar this way
        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutRejectedException exception)
        {
            throw new FetchFailedException(uri, FetchFailure.Timeout, $"{uri} timed out.", null, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FetchFailedException(uri, FetchFailure.Timeout, $"{uri} timed out.", null, exception);
        }
        catch (BrokenCircuitException exception)
        {
            throw new FetchFailedException(uri, FetchFailure.Network, $"{uri} skipped: too many recent failures (circuit open).", null, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new FetchFailedException(uri, FetchFailure.Network, $"{uri} failed: {exception.Message}", null, exception);
        }
    }

    private Uri NextHop(Uri original, Uri current, HttpResponseMessage response, int hop)
    {
        if (hop >= options.MaxRedirects)
        {
            throw new FetchFailedException(original, FetchFailure.Redirect, $"{original} redirected more than {options.MaxRedirects} times.");
        }

        var location = response.Headers.Location
            ?? throw new FetchFailedException(original, FetchFailure.Redirect, $"{current} redirected without a Location.");
        var next = location.IsAbsoluteUri ? location : new Uri(current, location);
        if (!IsHttp(next))
        {
            throw new FetchFailedException(original, FetchFailure.Redirect, $"{current} redirected to {next}, which isn't http or https.");
        }

        if (current.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp)
        {
            throw new FetchFailedException(original, FetchFailure.Redirect, $"{current} redirected from https to http.");
        }

        return next;
    }

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> within <see cref="FetcherOptions.BodyTimeout"/>. Over the cap the read
    /// fails, or with <paramref name="truncate"/> stops there (robots.txt). Content-Length isn't trusted on its own.
    /// </summary>
    private async Task<ReadOnlyMemory<byte>> ReadBodyAsync(Uri uri, HttpResponseMessage response, long maxBytes, bool truncate, CancellationToken cancellationToken)
    {
        if (!truncate && response.Content.Headers.ContentLength > maxBytes)
        {
            throw TooLarge(uri, maxBytes);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.BodyTimeout);
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    var room = maxBytes - buffer.Length;
                    if (read > room)
                    {
                        if (!truncate)
                        {
                            throw TooLarge(uri, maxBytes);
                        }

                        buffer.Write(chunk, 0, (int)room);
                        break;
                    }

                    buffer.Write(chunk, 0, read);
                }

                return buffer.ToArray();
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FetchFailedException(uri, FetchFailure.Timeout,
                string.Create(CultureInfo.InvariantCulture, $"{uri} took longer than {options.BodyTimeout} to send its body."), null, exception);
        }
        catch (IOException exception)
        {
            throw new FetchFailedException(uri, FetchFailure.Network, $"{uri} failed while sending its body: {exception.Message}", null, exception);
        }
    }

    private static FetchFailedException TooLarge(Uri uri, long maxBytes) =>
        new(uri, FetchFailure.TooLarge, string.Create(CultureInfo.InvariantCulture, $"{uri} is larger than the {maxBytes}-byte cap."));

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static bool IsHttp(Uri uri) =>
        uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
