namespace DcLedger.Infrastructure.Fetching;

/// <summary>How politely and how much the fetcher reads (SPEC §E, ADR-0003).</summary>
public sealed class FetcherOptions
{
    /// <summary>Sent on every request so site operators can see who we are and how to reach us.</summary>
    public string UserAgent { get; set; } = "dc-ledger/0.1 (+https://github.com/TooSmoove/dcledger)";

    /// <summary>Our product token for matching robots.txt groups.</summary>
    public string RobotsToken { get; set; } = "dc-ledger";

    /// <summary>Largest response body accepted, after decompression.</summary>
    public long MaxBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>How much of a robots.txt is read; RFC 9309 asks crawlers to parse at least 500 KiB.</summary>
    public int MaxRobotsBytes { get; set; } = 512 * 1024;

    /// <summary>Longest wait for a response body once the headers arrive (the resilience timeouts cover the headers).</summary>
    public TimeSpan BodyTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Minimum spacing between requests to the same host, retries included.</summary>
    public TimeSpan MinIntervalPerHost { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Redirects followed before giving up. Each hop is checked against robots.txt.</summary>
    public int MaxRedirects { get; set; } = 5;
}
