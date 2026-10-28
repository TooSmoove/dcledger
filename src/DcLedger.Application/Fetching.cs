using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>Fetches public web resources politely: robots.txt, per-host pacing, retries, size caps (ADR-0003, ADR-0005).</summary>
public interface IFetcher
{
    /// <summary>
    /// Fetches <paramref name="uri"/>, an absolute http or https URL. Throws <see cref="FetchFailedException"/> when it
    /// can't, after any retries.
    /// </summary>
    Task<FetchedResource> GetAsync(Uri uri, CancellationToken cancellationToken);
}

/// <summary>Bytes retrieved from the web, with the provenance every public item needs (SPEC AC2).</summary>
/// <param name="Content">The response body as received (after decompression). Untrusted.</param>
/// <param name="MediaType">The Content-Type media type, if the server sent one.</param>
/// <param name="FinalUri">The URI the bytes came from, after redirects.</param>
/// <param name="RetrievedAtUtc">When the response was read, in UTC.</param>
/// <param name="Sha256">SHA-256 of <paramref name="Content"/>.</param>
public sealed record FetchedResource(ReadOnlyMemory<byte> Content, string? MediaType, Uri FinalUri, DateTimeOffset RetrievedAtUtc, Sha256Hash Sha256);

/// <summary>Why a fetch failed.</summary>
public enum FetchFailure
{
    /// <summary>The site's robots.txt disallows the URL for us, or robots.txt couldn't be read (RFC 9309: assume disallowed).</summary>
    RobotsDisallowed,

    /// <summary>The response was larger than the size cap.</summary>
    TooLarge,

    /// <summary>The server answered with an error status, after retries where retrying makes sense.</summary>
    HttpStatus,

    /// <summary>The request timed out after retries, or the body took longer than allowed.</summary>
    Timeout,

    /// <summary>The connection failed, after retries.</summary>
    Network,

    /// <summary>Too many redirects, a redirect without a usable Location, to a non-http URL, or from https to http.</summary>
    Redirect,
}

/// <summary>A fetch that failed after the resilience policy gave up. Adapters translate it into source errors (ADR-0005).</summary>
public sealed class FetchFailedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public FetchFailedException(Uri uri, FetchFailure failure, string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Uri = uri;
        Failure = failure;
        StatusCode = statusCode;
    }

    /// <summary>Standard constructor; prefer the one that records the URI and failure.</summary>
    public FetchFailedException()
        : this(new Uri("about:blank"), FetchFailure.Network, "The fetch failed.")
    {
    }

    /// <summary>Standard constructor; prefer the one that records the URI and failure.</summary>
    public FetchFailedException(string message)
        : this(new Uri("about:blank"), FetchFailure.Network, message)
    {
    }

    /// <summary>Standard constructor; prefer the one that records the URI and failure.</summary>
    public FetchFailedException(string message, Exception innerException)
        : this(new Uri("about:blank"), FetchFailure.Network, message, null, innerException)
    {
    }

    /// <summary>The URI that was being fetched.</summary>
    public Uri Uri { get; }

    /// <summary>Why it failed.</summary>
    public FetchFailure Failure { get; }

    /// <summary>The HTTP status code, for <see cref="FetchFailure.HttpStatus"/>.</summary>
    public int? StatusCode { get; }
}
