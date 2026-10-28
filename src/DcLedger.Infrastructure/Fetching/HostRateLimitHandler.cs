using System.Threading.RateLimiting;

namespace DcLedger.Infrastructure.Fetching;

/// <summary>One token bucket per host, shared by every fetcher in the process.</summary>
internal sealed class HostRateLimiter : IDisposable
{
    public HostRateLimiter(FetcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Limiter = PartitionedRateLimiter.Create<HttpRequestMessage, string>(request =>
            RateLimitPartition.GetTokenBucketLimiter(
                request.RequestUri?.Authority ?? string.Empty,
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 1,
                    TokensPerPeriod = 1,
                    ReplenishmentPeriod = options.MinIntervalPerHost,
                    QueueLimit = 10_000,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                }));
    }

    public PartitionedRateLimiter<HttpRequestMessage> Limiter { get; }

    public void Dispose() => Limiter.Dispose();
}

/// <summary>
/// Waits for the host's token before every attempt. Registered inside the resilience handler, so retries are paced too;
/// time spent waiting counts toward the attempt timeout, so callers fetch from one host one request at a time.
/// </summary>
internal sealed class HostRateLimitHandler(HostRateLimiter rateLimiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var lease = await rateLimiter.Limiter.AcquireAsync(request, 1, cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            throw new HttpRequestException($"Rate limiter queue for {request.RequestUri?.Authority} is full.");
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
