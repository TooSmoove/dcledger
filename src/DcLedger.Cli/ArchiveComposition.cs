using DcLedger.Application;
using DcLedger.Infrastructure.Fetching;
using DcLedger.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DcLedger.Cli;

/// <summary>
/// Builds <see cref="ArchiveDocument"/> with the real fetcher and the private bucket (ADR-0001: the CLI is the only
/// composition root). Needs only the bucket's settings; a dry run needs none and gets a store that refuses to be used.
/// </summary>
internal static class ArchiveComposition
{
    /// <summary>Builds the services. Throws <see cref="ArgumentException"/> naming missing or malformed settings (exit code 2).</summary>
    internal static ServiceProvider Build(Func<string, string?> environment, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var services = new ServiceCollection();
        services.AddDcLedgerFetcher();
        services.AddTransient<ArchiveDocument>();

        if (dryRun)
        {
            services.AddSingleton<IDocumentStore>(new DryRunStore());
        }
        else
        {
            R2Environment.RequireAll(R2Environment.Variables, environment);
            services.AddDcLedgerR2Storage(R2Environment.Options(environment));
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>Stands in for the bucket in a dry run, which must never reach it.</summary>
    private sealed class DryRunStore : IDocumentStore
    {
        public Task<StoredDocument> PutAsync(ReadOnlyMemory<byte> content, string? mediaType, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A dry run must not use the private bucket.");
    }
}
