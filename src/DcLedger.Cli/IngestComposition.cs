using DcLedger.Adapters.Legistar;
using DcLedger.Application;
using DcLedger.Infrastructure.Fetching;
using DcLedger.Infrastructure.GitHub;
using DcLedger.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DcLedger.Cli;

/// <summary>
/// Builds <see cref="IngestJurisdiction"/> with its real ports (ADR-0001: the CLI is the only composition root).
/// Configuration comes from the environment: in Actions, repository variables and the <c>r2-docs</c> environment's
/// secrets (ADR-0003) and the job's token. A dry run needs none of it and gets ports that refuse to be used.
/// </summary>
internal static class IngestComposition
{
    /// <summary>The variables a real run needs. Values are never printed.</summary>
    internal static readonly IReadOnlyList<string> RequiredVariables =
    [
        "R2_ACCOUNT_ID",
        "R2_PRIVATE_BUCKET",
        "R2_PRIVATE_ACCESS_KEY_ID",
        "R2_PRIVATE_SECRET_ACCESS_KEY",
        "GITHUB_REPOSITORY",
        "GITHUB_TOKEN",
    ];

    /// <summary>
    /// Builds the services. Throws <see cref="ArgumentException"/> naming missing or malformed settings (exit code 2);
    /// the message never contains a secret.
    /// </summary>
    internal static ServiceProvider Build(Func<string, string?> environment, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var services = new ServiceCollection();
        services.AddDcLedgerFetcher();
        services.AddTransient<ISourceAdapter, LegistarAdapter>();
        services.AddTransient<IngestJurisdiction>();

        if (dryRun)
        {
            var none = new DryRunPorts();
            services.AddSingleton<IDocumentStore>(none);
            services.AddSingleton<IIngestState>(none);
            services.AddSingleton<IReviewQueue>(none);
            services.AddSingleton<IRunLog>(none);
        }
        else
        {
            var missing = RequiredVariables.Where(name => string.IsNullOrWhiteSpace(environment(name))).ToList();
            if (missing.Count > 0)
            {
                throw new ArgumentException($"Missing environment variables: {string.Join(", ", missing)}.", nameof(environment));
            }

            services.AddDcLedgerR2Storage(new R2Options
            {
                AccountId = environment("R2_ACCOUNT_ID")!.Trim(),
                Bucket = environment("R2_PRIVATE_BUCKET")!.Trim(),
                AccessKeyId = environment("R2_PRIVATE_ACCESS_KEY_ID")!.Trim(),
                SecretAccessKey = environment("R2_PRIVATE_SECRET_ACCESS_KEY")!.Trim(),
            });
            services.AddDcLedgerGitHubReviewQueue(new GitHubOptions
            {
                Repository = environment("GITHUB_REPOSITORY")!.Trim(),
                Token = environment("GITHUB_TOKEN")!.Trim(),
            });
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>Stands in for the private ports in a dry run, which must never reach them.</summary>
    private sealed class DryRunPorts : IDocumentStore, IIngestState, IReviewQueue, IRunLog
    {
        public Task<StoredDocument> PutAsync(ReadOnlyMemory<byte> content, string? mediaType, CancellationToken cancellationToken) => throw Refused();

        public Task<MeetingState?> GetAsync(MeetingKey meeting, CancellationToken cancellationToken) => throw Refused();

        public Task PutAsync(MeetingKey meeting, MeetingState state, CancellationToken cancellationToken) => throw Refused();

        public Task<int> OpenAsync(ReviewIssue issue, CancellationToken cancellationToken) => throw Refused();

        public Task<bool> CommentAsync(int issueNumber, string body, CancellationToken cancellationToken) => throw Refused();

        public Task WriteAsync(RunRecord run, CancellationToken cancellationToken) => throw Refused();

        private static InvalidOperationException Refused() => new("A dry run must not use the private bucket or the review queue.");
    }
}
