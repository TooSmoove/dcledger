using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using DcLedger.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace DcLedger.Infrastructure.GitHub;

/// <summary>Registers the GitHub issues review queue.</summary>
public static partial class GitHubServiceCollectionExtensions
{
    private const string ApiVersion = "2022-11-28";

    /// <summary>
    /// Adds <see cref="IReviewQueue"/>. Throws <see cref="ArgumentException"/> for incomplete or malformed options (exit
    /// code 2); the message never contains the token. <paramref name="configureResilience"/> exists for tests.
    /// </summary>
    public static IServiceCollection AddDcLedgerGitHubReviewQueue(
        this IServiceCollection services,
        GitHubOptions options,
        Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);
        var settings = new GitHubOptions // a copy, so later changes to the caller's object can't redirect the queue
        {
            Repository = options.Repository,
            Token = options.Token,
            Labels = [.. options.Labels],
            ApiBaseUri = options.ApiBaseUri,
        };

        services.AddHttpClient(nameof(GitHubReviewQueue), http =>
            {
                http.BaseAddress = settings.ApiBaseUri;
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("dc-ledger/0.1 (+https://ncdcledger.org/about/)");
            })
            .AddTypedClient<IReviewQueue>(http => new GitHubReviewQueue(http, settings))
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.Retry.DisableForUnsafeHttpMethods(); // never re-send a create
                configureResilience?.Invoke(resilience);
            });
        return services;
    }

    private static void Validate(GitHubOptions options)
    {
        if (!RepositoryPattern().IsMatch(options.Repository))
        {
            throw new ArgumentException("GitHub repository must look like owner/repo.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Token))
        {
            throw new ArgumentException("A GitHub token is required.", nameof(options));
        }

        if (options.ApiBaseUri is not { IsAbsoluteUri: true } api || (api.Scheme != Uri.UriSchemeHttps && !api.IsLoopback))
        {
            throw new ArgumentException("The GitHub API address must be https.", nameof(options));
        }

        if (options.Labels.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Labels can't be empty.", nameof(options));
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9-]{1,39}/[A-Za-z0-9._-]{1,100}\z", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();
}
