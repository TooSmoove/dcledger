using System.Net;
using DcLedger.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace DcLedger.Infrastructure.Fetching;

/// <summary>Registers <see cref="IFetcher"/> with the standard resilience handler and per-host pacing.</summary>
public static class FetcherServiceCollectionExtensions
{
    /// <summary>
    /// Adds the fetcher. Defaults: 3 retries with jittered backoff on 408, 429 and 5xx (Retry-After honored; never 401,
    /// 403 or 404), 30 s per attempt, 2 min per fetch, one request per host every 500 ms, 50 MB cap, no automatic
    /// redirects (each hop is checked against robots.txt). The callbacks exist for tests and must keep those intents.
    /// </summary>
    public static IServiceCollection AddDcLedgerFetcher(
        this IServiceCollection services,
        Action<FetcherOptions>? configure = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new FetcherOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<HostRateLimiter>();
        services.AddTransient<HostRateLimitHandler>();

        var client = services.AddHttpClient<IFetcher, HttpFetcher>()
            .ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan) // the resilience timeouts govern
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        client.AddStandardResilienceHandler(resilience =>
        {
            resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
            resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
            resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(1);
            configureResilience?.Invoke(resilience);
        });

        // Added after the resilience handler, so it sits inside it and paces every retry as well.
        client.AddHttpMessageHandler<HostRateLimitHandler>();
        return services;
    }
}
