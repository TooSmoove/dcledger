using System.Text.Json;
using DcLedger.Application;
using DcLedger.Infrastructure.GitHub;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace DcLedger.Infrastructure.Tests.GitHub;

public sealed class GitHubReviewQueueTests : IDisposable
{
    private const string Token = "test-token-never-printed";
    private const string Issues = "/repos/TooSmoove/dcledger/issues";

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly List<ServiceProvider> _providers = [];

    [Fact]
    public async Task Open_sends_title_body_and_labels_with_the_api_headers()
    {
        _server.Given(Request.Create().WithPath(Issues).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("""{"number":52,"state":"open","title":"ignored"}"""));

        var number = await Queue().OpenAsync(new ReviewIssue("Review: x", "body text"), Ct);

        number.ShouldBe(52);
        var request = Single(Issues, "POST");
        request.Headers!["Authorization"].ShouldHaveSingleItem().ShouldBe("Bearer " + Token);
        request.Headers["Accept"].ShouldHaveSingleItem().ShouldBe("application/vnd.github+json");
        request.Headers["X-GitHub-Api-Version"].ShouldHaveSingleItem().ShouldBe("2022-11-28");
        request.Headers["User-Agent"].ShouldHaveSingleItem().ShouldStartWith("dc-ledger/");
        using var json = JsonDocument.Parse(request.Body!);
        json.RootElement.GetProperty("title").GetString().ShouldBe("Review: x");
        json.RootElement.GetProperty("body").GetString().ShouldBe("body text");
        string[] labels = ["review"];
        json.RootElement.GetProperty("labels").EnumerateArray().Select(label => label.GetString()!).ShouldBe(labels);
    }

    [Fact]
    public async Task Comment_on_an_open_issue_only_comments()
    {
        IssueState(52, "open");
        Comments(52);

        var reopened = await Queue().CommentAsync(52, "agenda changed", Ct);

        reopened.ShouldBeFalse();
        string[] expected = ["GET", "POST"];
        Methods().ShouldBe(expected);
        using var json = JsonDocument.Parse(Single($"{Issues}/52/comments", "POST").Body!);
        json.RootElement.GetProperty("body").GetString().ShouldBe("agenda changed");
    }

    [Fact]
    public async Task Comment_on_a_closed_issue_reopens_it_first()
    {
        IssueState(52, "closed");
        _server.Given(Request.Create().WithPath($"{Issues}/52").UsingPatch())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"number":52,"state":"open"}"""));
        Comments(52);

        var reopened = await Queue().CommentAsync(52, "agenda changed", Ct);

        reopened.ShouldBeTrue();
        string[] expected = ["GET", "PATCH", "POST"];
        Methods().ShouldBe(expected);
        using var json = JsonDocument.Parse(Single($"{Issues}/52", "PATCH").Body!);
        json.RootElement.GetProperty("state").GetString().ShouldBe("open");
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task Refusal_is_a_queue_failure_with_the_status_and_message_but_never_the_token(int status)
    {
        _server.Given(Request.Create().WithPath(Issues).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status).WithBody("""{"message":"Validation Failed"}"""));

        var failure = await Should.ThrowAsync<ReviewQueueFailedException>(() => Queue().OpenAsync(new ReviewIssue("t", "b"), Ct));

        failure.StatusCode.ShouldBe(status);
        failure.Message.ShouldContain("Validation Failed");
        failure.Message.ShouldNotContain(Token);
    }

    [Fact]
    public async Task Creating_an_issue_is_never_retried()
    {
        _server.Given(Request.Create().WithPath(Issues).UsingPost()).RespondWith(Response.Create().WithStatusCode(502));

        await Should.ThrowAsync<ReviewQueueFailedException>(() => Queue().OpenAsync(new ReviewIssue("t", "b"), Ct));

        _server.LogEntries.Count(entry => entry.RequestMessage?.Path == Issues).ShouldBe(1);
    }

    [Fact]
    public async Task Reading_an_issue_is_retried()
    {
        _server.Given(Request.Create().WithPath($"{Issues}/52").UsingGet()).InScenario("flaky").WillSetStateTo("up")
            .RespondWith(Response.Create().WithStatusCode(503));
        _server.Given(Request.Create().WithPath($"{Issues}/52").UsingGet()).InScenario("flaky").WhenStateIs("up")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"number":52,"state":"open"}"""));
        Comments(52);

        (await Queue().CommentAsync(52, "x", Ct)).ShouldBeFalse();

        string[] expected = ["GET", "GET", "POST"];
        Methods().ShouldBe(expected);
    }

    [Fact]
    public async Task Body_over_githubs_limit_is_refused_before_sending()
    {
        var body = new string('x', GitHubReviewQueue.MaxBodyLength + 1);

        await Should.ThrowAsync<ArgumentException>(() => Queue().OpenAsync(new ReviewIssue("t", body), Ct));

        _server.LogEntries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("token")]
    [InlineData("api")]
    [InlineData("labels")]
    public void Bad_options_are_rejected_without_echoing_the_token(string broken)
    {
        var options = Options();
        switch (broken)
        {
            case "repository":
                options.Repository = "not a repo";
                break;
            case "token":
                options.Token = " ";
                break;
            case "api":
                options.ApiBaseUri = new Uri("http://api.example.org/");
                break;
            default:
                options.Labels = [" "];
                break;
        }

        var failure = Should.Throw<ArgumentException>(() => new ServiceCollection().AddDcLedgerGitHubReviewQueue(options));

        failure.Message.ShouldNotContain(Token);
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

    private GitHubOptions Options() => new()
    {
        Repository = "TooSmoove/dcledger",
        Token = Token,
        ApiBaseUri = new Uri(_server.Url! + "/"),
    };

    private IReviewQueue Queue()
    {
        var provider = new ServiceCollection()
            .AddDcLedgerGitHubReviewQueue(Options(), resilience =>
            {
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(1);
                resilience.Retry.BackoffType = DelayBackoffType.Constant;
                resilience.Retry.UseJitter = false;
            })
            .BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<IReviewQueue>();
    }

    private void IssueState(int number, string state) =>
        _server.Given(Request.Create().WithPath($"{Issues}/{number}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody($$"""{"number":{{number}},"state":"{{state}}"}"""));

    private void Comments(int number) =>
        _server.Given(Request.Create().WithPath($"{Issues}/{number}/comments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("""{"id":1}"""));

    private string[] Methods() => [.. _server.LogEntries.Select(entry => entry.RequestMessage!.Method)];

    private WireMock.IRequestMessage Single(string path, string method) =>
        _server.LogEntries.Select(entry => entry.RequestMessage!).Single(request => request.Path == path && request.Method == method);
}
