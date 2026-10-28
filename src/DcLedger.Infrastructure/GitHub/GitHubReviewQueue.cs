using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DcLedger.Application;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace DcLedger.Infrastructure.GitHub;

/// <summary>
/// <see cref="IReviewQueue"/> over the GitHub REST API. Creating an issue or comment is never retried, so a timeout
/// can't open a duplicate; reads are retried by the standard resilience handler.
/// </summary>
internal sealed class GitHubReviewQueue(HttpClient client, GitHubOptions options) : IReviewQueue
{
    /// <summary>GitHub's limit on an issue or comment body.</summary>
    internal const int MaxBodyLength = 65_536;

    public async Task<int> OpenAsync(ReviewIssue issue, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issue);
        RequireFits(issue.Body);
        var content = JsonContent.Create(new CreateIssueRequest(issue.Title, issue.Body, options.Labels), GitHubJsonContext.Default.CreateIssueRequest);
        using var request = Request(HttpMethod.Post, "issues", content);
        var created = await SendAsync(request, GitHubJsonContext.Default.IssueResponse, cancellationToken).ConfigureAwait(false);
        return created.Number;
    }

    public async Task<bool> CommentAsync(int issueNumber, string body, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(issueNumber);
        ArgumentNullException.ThrowIfNull(body);
        RequireFits(body);
        var path = string.Create(CultureInfo.InvariantCulture, $"issues/{issueNumber}");

        IssueResponse issue;
        using (var read = Request(HttpMethod.Get, path))
        {
            issue = await SendAsync(read, GitHubJsonContext.Default.IssueResponse, cancellationToken).ConfigureAwait(false);
        }

        var reopen = issue.State == "closed";
        if (reopen)
        {
            var state = JsonContent.Create(new UpdateIssueRequest("open"), GitHubJsonContext.Default.UpdateIssueRequest);
            using var update = Request(HttpMethod.Patch, path, state);
            await SendAsync(update, GitHubJsonContext.Default.IssueResponse, cancellationToken).ConfigureAwait(false);
        }

        var comment = JsonContent.Create(new CreateCommentRequest(body), GitHubJsonContext.Default.CreateCommentRequest);
        using var create = Request(HttpMethod.Post, path + "/comments", comment);
        await SendAsync(create, GitHubJsonContext.Default.CommentResponse, cancellationToken).ConfigureAwait(false);
        return reopen;
    }

    private HttpRequestMessage Request(HttpMethod method, string path, HttpContent? content = null) =>
        new(method, new Uri($"repos/{options.Repository}/{path}", UriKind.Relative)) { Content = content };

    private async Task<T> SendAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> responseType, CancellationToken cancellationToken)
        where T : class
    {
        var what = $"{request.Method} {request.RequestUri}";
        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                var reason = await ErrorMessageAsync(response, cancellationToken).ConfigureAwait(false);
                throw new ReviewQueueFailedException(string.Create(CultureInfo.InvariantCulture, $"GitHub {what} answered HTTP {status}: {reason}"), status);
            }

            return await response.Content.ReadFromJsonAsync(responseType, cancellationToken).ConfigureAwait(false)
                ?? throw new ReviewQueueFailedException($"GitHub {what} sent an empty response.", status);
        }
        catch (JsonException exception)
        {
            throw new ReviewQueueFailedException($"GitHub {what} sent an unexpected response: {exception.Message}", null, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ReviewQueueFailedException($"GitHub {what} failed: {exception.Message}", null, exception);
        }
        catch (TimeoutRejectedException exception)
        {
            throw new ReviewQueueFailedException($"GitHub {what} timed out.", null, exception);
        }
        catch (BrokenCircuitException exception)
        {
            throw new ReviewQueueFailedException($"GitHub {what} skipped: too many recent failures.", null, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReviewQueueFailedException($"GitHub {what} timed out.", null, exception);
        }
    }

    private static async Task<string> ErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync(GitHubJsonContext.Default.ErrorResponse, cancellationToken).ConfigureAwait(false);
            var message = error?.Message ?? "no message";
            return message.Length > 200 ? message[..200] : message;
        }
        catch (JsonException)
        {
            return "no message";
        }
    }

    private static void RequireFits(string body)
    {
        if (body.Length > MaxBodyLength)
        {
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Body is {body.Length} characters; GitHub accepts {MaxBodyLength}."), nameof(body));
        }
    }
}
