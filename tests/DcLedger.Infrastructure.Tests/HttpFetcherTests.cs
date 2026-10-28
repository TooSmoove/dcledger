using System.Diagnostics;
using System.Net;
using System.Text;
using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Infrastructure.Fetching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace DcLedger.Infrastructure.Tests;

public sealed class HttpFetcherTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 23, 0, 0, TimeSpan.Zero);

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly List<ServiceProvider> _providers = [];

    [Fact]
    public async Task Fetch_returns_bytes_media_type_final_uri_time_and_hash()
    {
        NoRobots();
        _server.Given(Get("/v1/guilford/events"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("[]"));

        var result = await Fetcher().GetAsync(Url("/v1/guilford/events"), Ct);

        Encoding.UTF8.GetString(result.Content.Span).ShouldBe("[]");
        result.MediaType.ShouldBe("application/json");
        result.FinalUri.ShouldBe(Url("/v1/guilford/events"));
        result.RetrievedAtUtc.ShouldBe(Now);
        result.Sha256.ShouldBe(Sha256Hash.Compute("[]"u8));
    }

    [Fact]
    public async Task Requests_identify_us_with_the_user_agent()
    {
        NoRobots();
        _server.Given(Get("/x")).RespondWith(Response.Create().WithStatusCode(200));

        await Fetcher().GetAsync(Url("/x"), Ct);

        var headers = _server.LogEntries.Last(entry => entry.RequestMessage?.Path == "/x").RequestMessage?.Headers;
        headers.ShouldNotBeNull();
        var userAgent = headers["User-Agent"].ShouldHaveSingleItem();
        userAgent.ShouldStartWith("dc-ledger/");
        userAgent.ShouldContain("(+https://ncdcledger.org/about/)");
    }

    [Fact]
    public async Task Server_error_is_retried_until_it_succeeds()
    {
        NoRobots();
        _server.Given(Get("/flaky")).InScenario("flaky").WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(503));
        _server.Given(Get("/flaky")).InScenario("flaky").WhenStateIs("recovered")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        var result = await Fetcher().GetAsync(Url("/flaky"), Ct);

        Encoding.UTF8.GetString(result.Content.Span).ShouldBe("ok");
        Requests("/flaky").ShouldBe(2);
    }

    [Fact]
    public async Task Too_many_requests_honors_retry_after()
    {
        NoRobots();
        _server.Given(Get("/busy")).InScenario("busy").WillSetStateTo("free")
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "1"));
        _server.Given(Get("/busy")).InScenario("busy").WhenStateIs("free")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));
        var clock = Stopwatch.StartNew();

        await Fetcher().GetAsync(Url("/busy"), Ct);

        clock.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
        Requests("/busy").ShouldBe(2);
    }

    [Fact]
    public async Task Not_found_is_not_retried()
    {
        NoRobots();
        _server.Given(Get("/missing")).RespondWith(Response.Create().WithStatusCode(404));

        var failure = await Should.ThrowAsync<FetchFailedException>(() => Fetcher().GetAsync(Url("/missing"), Ct));

        failure.Failure.ShouldBe(FetchFailure.HttpStatus);
        failure.StatusCode.ShouldBe(404);
        Requests("/missing").ShouldBe(1);
    }

    [Fact]
    public async Task Robots_disallow_stops_the_fetch_before_any_request()
    {
        _server.Given(Get("/robots.txt")).RespondWith(Response.Create().WithStatusCode(200).WithBody("User-agent: *\nDisallow: /private"));
        _server.Given(Get("/private/doc")).RespondWith(Response.Create().WithStatusCode(200));

        var failure = await Should.ThrowAsync<FetchFailedException>(() => Fetcher().GetAsync(Url("/private/doc"), Ct));

        failure.Failure.ShouldBe(FetchFailure.RobotsDisallowed);
        Requests("/private/doc").ShouldBe(0);
    }

    [Fact]
    public async Task Unreachable_robots_txt_is_treated_as_disallow_all()
    {
        _server.Given(Get("/robots.txt")).RespondWith(Response.Create().WithStatusCode(503));
        _server.Given(Get("/agenda")).RespondWith(Response.Create().WithStatusCode(200));

        var failure = await Should.ThrowAsync<FetchFailedException>(() => Fetcher().GetAsync(Url("/agenda"), Ct));

        failure.Failure.ShouldBe(FetchFailure.RobotsDisallowed);
        Requests("/agenda").ShouldBe(0);
    }

    [Fact]
    public async Task Robots_txt_is_read_once_per_site()
    {
        NoRobots();
        _server.Given(Get("/a")).RespondWith(Response.Create().WithStatusCode(200));
        var fetcher = Fetcher();

        await fetcher.GetAsync(Url("/a"), Ct);
        await fetcher.GetAsync(Url("/a"), Ct);

        Requests("/robots.txt").ShouldBe(1);
    }

    [Fact]
    public async Task Response_over_the_cap_is_rejected()
    {
        NoRobots();
        var body = new byte[2048];
        _server.Given(Get("/big")).RespondWith(Response.Create().WithStatusCode(200).WithBody(body));

        var failure = await Should.ThrowAsync<FetchFailedException>(() => Fetcher(maxBytes: 1024).GetAsync(Url("/big"), Ct));

        failure.Failure.ShouldBe(FetchFailure.TooLarge);
    }

    [Fact]
    public async Task Response_over_the_cap_without_a_content_length_is_rejected_while_reading()
    {
        var fetcher = Fetcher(maxBytes: 1024, primary: new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnsizedContent(new byte[2048], TimeSpan.Zero),
        }));

        var failure = await Should.ThrowAsync<FetchFailedException>(() => fetcher.GetAsync(new Uri("https://example.org/big"), Ct));

        failure.Failure.ShouldBe(FetchFailure.TooLarge);
    }

    [Fact]
    public async Task Slow_body_times_out()
    {
        var fetcher = Fetcher(bodyTimeout: TimeSpan.FromMilliseconds(100), primary: new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnsizedContent(new byte[16], TimeSpan.FromSeconds(30)),
        }));

        var failure = await Should.ThrowAsync<FetchFailedException>(() => fetcher.GetAsync(new Uri("https://example.org/slow"), Ct));

        failure.Failure.ShouldBe(FetchFailure.Timeout);
    }

    [Fact]
    public async Task Slow_server_times_out_after_three_retries()
    {
        var attempts = 0;
        var fetcher = Fetcher(
            resilience: options => options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(100),
            primary: new StubHandler(async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref attempts);
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }));

        var failure = await Should.ThrowAsync<FetchFailedException>(() => fetcher.GetAsync(new Uri("https://example.org/stall"), Ct));

        failure.Failure.ShouldBe(FetchFailure.Timeout);
        attempts.ShouldBe(4);
    }

    [Fact]
    public async Task Redirect_is_followed_and_the_final_uri_reported()
    {
        NoRobots();
        _server.Given(Get("/old")).RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", "/new"));
        _server.Given(Get("/new")).RespondWith(Response.Create().WithStatusCode(200).WithBody("moved"));

        var result = await Fetcher().GetAsync(Url("/old"), Ct);

        result.FinalUri.ShouldBe(Url("/new"));
        Encoding.UTF8.GetString(result.Content.Span).ShouldBe("moved");
    }

    [Fact]
    public async Task Redirect_loop_gives_up()
    {
        NoRobots();
        _server.Given(Get("/loop")).RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", "/loop"));

        var failure = await Should.ThrowAsync<FetchFailedException>(() => Fetcher().GetAsync(Url("/loop"), Ct));

        failure.Failure.ShouldBe(FetchFailure.Redirect);
    }

    [Fact]
    public async Task Redirect_to_another_scheme_is_refused()
    {
        NoRobots();
        _server.Given(Get("/away")).RespondWith(Response.Create().WithStatusCode(301).WithHeader("Location", "ftp://example.org/file"));

        var failure = await Should.ThrowAsync<FetchFailedException>(() => Fetcher().GetAsync(Url("/away"), Ct));

        failure.Failure.ShouldBe(FetchFailure.Redirect);
    }

    [Fact]
    public async Task Requests_to_one_host_are_spaced_out()
    {
        NoRobots();
        _server.Given(Get("/paced")).RespondWith(Response.Create().WithStatusCode(200));
        var fetcher = Fetcher(interval: TimeSpan.FromMilliseconds(150));
        await fetcher.GetAsync(Url("/paced"), Ct); // robots.txt + first request use the first tokens
        var clock = Stopwatch.StartNew();

        await fetcher.GetAsync(Url("/paced"), Ct);
        await fetcher.GetAsync(Url("/paced"), Ct);

        clock.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task Non_http_url_is_a_caller_error()
    {
        await Should.ThrowAsync<ArgumentException>(() => Fetcher().GetAsync(new Uri("file:///etc/passwd"), Ct));
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _server.Stop();
        _server.Dispose();
        GC.SuppressFinalize(this);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IRequestBuilder Get(string path) => Request.Create().WithPath(path).UsingGet();

    /// <summary>The production registration with short delays; <paramref name="primary"/> replaces the network.</summary>
    private IFetcher Fetcher(
        long maxBytes = 1024 * 1024,
        TimeSpan? interval = null,
        TimeSpan? bodyTimeout = null,
        Action<HttpStandardResilienceOptions>? resilience = null,
        HttpMessageHandler? primary = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FixedTimeProvider(Now))
            .AddDcLedgerFetcher(
                options =>
                {
                    options.MaxBytes = maxBytes;
                    options.MinIntervalPerHost = interval ?? TimeSpan.FromMilliseconds(1);
                    options.BodyTimeout = bodyTimeout ?? TimeSpan.FromSeconds(30);
                },
                options =>
                {
                    options.Retry.Delay = TimeSpan.FromMilliseconds(1);
                    options.Retry.BackoffType = DelayBackoffType.Constant;
                    options.Retry.UseJitter = false;
                    resilience?.Invoke(options);
                });
        if (primary is not null)
        {
            services.AddHttpClient<IFetcher, HttpFetcher>().ConfigurePrimaryHttpMessageHandler(() => primary);
        }

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<IFetcher>();
    }

    private void NoRobots() => _server.Given(Get("/robots.txt")).RespondWith(Response.Create().WithStatusCode(404));

    private int Requests(string path) => _server.LogEntries.Count(entry => entry.RequestMessage?.Path == path);

    private Uri Url(string pathAndQuery) => new(_server.Url + pathAndQuery);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Answers robots.txt with 404 and everything else with <paramref name="respond"/>.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public StubHandler(Func<HttpResponseMessage> respond)
            : this((_, _) => Task.FromResult(respond()))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsolutePath == "/robots.txt"
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
                : respond(request, cancellationToken);
    }

    /// <summary>A body with no Content-Length that starts after <paramref name="delay"/>.</summary>
    private sealed class UnsizedContent(byte[] bytes, TimeSpan delay) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
