using System.Text;
using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Infrastructure.Ledger;
using DcLedger.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace DcLedger.Cli.Tests;

/// <summary>dcl archive-document (issue #25) with an in-memory fetcher and bucket.</summary>
public sealed class ArchiveDocumentCommandTests : IDisposable
{
    private const string Url = "https://fixture-county.example.org/meetings/1600/actions/7";
    private static readonly byte[] Page = Encoding.UTF8.GetBytes("<html>fixture action detail</html>");

    private readonly string _temp = Directory.CreateTempSubdirectory("dcl-archive-").FullName;
    private readonly FixedTime _time = new(new DateTimeOffset(2026, 11, 6, 13, 0, 0, TimeSpan.Zero));
    private readonly FakeFetcher _fetcher;
    private readonly MemoryDocumentStore _store = new();
    private readonly List<bool> _composedDryRun = [];

    public ArchiveDocumentCommandTests()
    {
        _fetcher = new FakeFetcher(_time);
        _fetcher.Responses[new Uri(Url)] = (Page, "text/html");
    }

    [Fact]
    public async Task Stores_the_document_and_prints_a_record_the_ledger_accepts()
    {
        var result = await ArchiveAsync("--url", Url, "--source-type", "official");

        result.Code.ShouldBe(ExitCodes.Ok, result.Error);
        var sha256 = Sha256Hash.Compute(Page);
        _store.Documents.Keys.ShouldBe([sha256]);
        result.Output.ShouldContain($"Retrieved {Url}: {Page.Length} bytes, stored in the private bucket.");
        result.Output.ShouldContain($"Add this file as data/documents/{sha256.Value}.yaml:");

        // The printed record, pasted as is, loads and validates as a ledger document.
        var record = result.Output[(result.Output.IndexOf("id: ", StringComparison.Ordinal))..];
        var documents = Path.Combine(_temp, "documents");
        Directory.CreateDirectory(documents);
        await File.WriteAllTextAsync(Path.Combine(documents, $"{sha256.Value}.yaml"), record, TestContext.Current.CancellationToken);
        var snapshot = new YamlLedgerReader().Load(_temp);
        LedgerValidator.Validate(snapshot, []).ShouldBeEmpty();
        var document = snapshot.Documents.ShouldHaveSingleItem().Record;
        document.Id.ShouldBe(sha256);
        document.Url.ShouldBe(new Uri(Url));
        document.RetrievedAtUtc.ShouldBe(_time.Now);
        document.SourceType.ShouldBe(SourceType.Official);
    }

    [Fact]
    public async Task A_second_run_reports_the_document_already_stored()
    {
        await ArchiveAsync("--url", Url, "--source-type", "news");

        var again = await ArchiveAsync("--url", Url, "--source-type", "news");

        again.Code.ShouldBe(ExitCodes.Ok, again.Error);
        again.Output.ShouldContain("already in the private bucket.");
        again.Output.ShouldContain("source_type: news");
        _store.Writes.ShouldBe(1);
    }

    [Fact]
    public async Task A_dry_run_composes_without_the_bucket_and_stores_nothing()
    {
        var result = await ArchiveAsync("--url", Url, "--source-type", "official", "--dry-run");

        result.Code.ShouldBe(ExitCodes.Ok, result.Error);
        _composedDryRun.ShouldBe([true]);
        result.Output.ShouldContain("not stored (dry run).");
    }

    [Theory]
    [InlineData("http://fixture-county.example.org/a")]
    [InlineData("ftp://fixture-county.example.org/a")]
    [InlineData("https://user:password@fixture-county.example.org/a")]
    [InlineData("fixture-county.example.org/a")]
    public async Task Only_plain_https_urls_are_accepted(string url)
    {
        var result = await ArchiveAsync("--url", url, "--source-type", "official");

        result.Code.ShouldBe(ExitCodes.Usage);
        result.Error.ShouldContain("--url must be an absolute https URL");
        _fetcher.Requests.ShouldBeEmpty();
        _composedDryRun.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_source_type_is_a_usage_error()
    {
        (await ArchiveAsync("--url", Url, "--source-type", "blog")).Code.ShouldBe(ExitCodes.Usage);
    }

    [Fact]
    public async Task A_failed_retrieval_exits_3_with_an_annotation_and_no_record()
    {
        var result = await ArchiveAsync("--url", "https://fixture-county.example.org/missing", "--source-type", "official", "--format", "github");

        result.Code.ShouldBe(ExitCodes.ExternalFailure);
        result.Output.ShouldStartWith("::error title=archive-document::Retrieval failed (HttpStatus)");
        result.Output.ShouldNotContain("id: ");
        _store.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_configuration_exits_2_and_names_the_bucket_variables_only()
    {
        var result = await DclRunner.RunWithAsync(
            (output, error) => CliApp.CreateRootCommand(output, error, _ => throw new InvalidOperationException("not ingest"), dryRun => ArchiveComposition.Build(_ => null, dryRun)),
            "archive-document", "--url", Url, "--source-type", "official");

        result.Code.ShouldBe(ExitCodes.Usage);
        result.Error.ShouldContain(
            "Configuration: Missing environment variables: R2_ACCOUNT_ID, R2_PRIVATE_BUCKET, R2_PRIVATE_ACCESS_KEY_ID, R2_PRIVATE_SECRET_ACCESS_KEY.");
    }

    public void Dispose()
    {
        Directory.Delete(_temp, recursive: true);
        GC.SuppressFinalize(this);
    }

    private Task<(int Code, string Output, string Error)> ArchiveAsync(params string[] args) =>
        DclRunner.RunWithAsync(
            (output, error) => CliApp.CreateRootCommand(output, error, _ => throw new InvalidOperationException("not ingest"), Compose),
            ["archive-document", .. args]);

    private ServiceProvider Compose(bool dryRun)
    {
        _composedDryRun.Add(dryRun);
        var services = new ServiceCollection();
        services.AddSingleton<IFetcher>(_fetcher);
        services.AddSingleton<IDocumentStore>(_store);
        services.AddTransient<ArchiveDocument>();
        return services.BuildServiceProvider();
    }
}
