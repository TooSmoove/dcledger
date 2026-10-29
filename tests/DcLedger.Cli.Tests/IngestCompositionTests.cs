using DcLedger.Application;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace DcLedger.Cli.Tests;

public sealed class IngestCompositionTests
{
    private const string Secret = "do-not-print-this-secret";

    private static readonly Dictionary<string, string> Complete = new(StringComparer.Ordinal)
    {
        ["R2_ACCOUNT_ID"] = "0123456789abcdef0123456789abcdef",
        ["R2_PRIVATE_BUCKET"] = "dcledger-private",
        ["R2_PRIVATE_ACCESS_KEY_ID"] = "access-key-id",
        ["R2_PRIVATE_SECRET_ACCESS_KEY"] = Secret,
        ["GITHUB_REPOSITORY"] = "TooSmoove/dcledger",
        ["GITHUB_TOKEN"] = Secret,
    };

    [Fact]
    public async Task Dry_run_needs_no_configuration()
    {
        var provider = IngestComposition.Build(_ => null, dryRun: true);
        await using (provider)
        {
            provider.GetRequiredService<IngestJurisdiction>().ShouldNotBeNull();
            await Should.ThrowAsync<InvalidOperationException>(() => provider.GetRequiredService<IReviewQueue>().OpenAsync(new ReviewIssue("t", "b"), TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Real_run_composes_from_complete_configuration_without_touching_the_network()
    {
        var provider = IngestComposition.Build(name => Complete.GetValueOrDefault(name), dryRun: false);
        await using (provider)
        {
            provider.GetRequiredService<IngestJurisdiction>().ShouldNotBeNull();
        }
    }

    [Fact]
    public void Missing_settings_are_named_and_malformed_ones_refused_without_printing_secrets()
    {
        var missing = Should.Throw<ArgumentException>(() => IngestComposition.Build(name => name == "GITHUB_TOKEN" ? null : Complete[name], dryRun: false));
        missing.Message.ShouldStartWith("Missing environment variables: GITHUB_TOKEN.");

        var malformed = Should.Throw<ArgumentException>(() => IngestComposition.Build(name => name == "R2_PRIVATE_BUCKET" ? "Not_A_Bucket" : Complete[name], dryRun: false));
        malformed.Message.ShouldContain("bucket");
        malformed.Message.ShouldNotContain(Secret);
    }
}
