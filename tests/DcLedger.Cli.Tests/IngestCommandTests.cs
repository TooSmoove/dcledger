using System.Text;
using DcLedger.Adapters.Legistar;
using DcLedger.Application;
using DcLedger.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace DcLedger.Cli.Tests;

/// <summary>
/// dcl ingest end to end over the recorded Guilford responses (tests/fixtures/legistar/guilford), with the real Legistar
/// adapter and in-memory stand-ins for the private bucket, the review queue and the run log.
/// </summary>
public sealed class IngestCommandTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 28, 12, 29, 30, TimeSpan.Zero);

    private readonly string _temp = Directory.CreateTempSubdirectory("dcl-ingest-").FullName;
    private readonly FixedTime _time = new(Morning);
    private readonly FakeFetcher _fetcher;
    private readonly MemoryDocumentStore _documents = new();
    private readonly MemoryIngestState _state = new();
    private readonly MemoryReviewQueue _queue = new();
    private readonly MemoryRunLog _runLog = new();
    private readonly List<bool> _composedDryRun = [];
    private FakeSource? _source;

    public IngestCommandTests()
    {
        _fetcher = new FakeFetcher(_time) { Fallback = RecordedGuilford };
    }

    [Fact]
    public async Task Recorded_meetings_open_one_issue_and_an_immediate_rerun_opens_none()
    {
        var first = await IngestAsync("--run-id", "40623358875-1");
        _time.Now = Morning.AddMinutes(5);
        var second = await IngestAsync("--run-id", "40623358876-1");

        first.Code.ShouldBe(ExitCodes.Ok, first.Error);
        first.Output.ShouldContain("guilford: meetings dated 2026-09-28 to 2026-12-27, run 40623358875-1.");
        first.Output.ShouldContain("2026-10-15 legistar/guilford/1599 Board of Commissioners: opened issue #1 (rules: data_center).");
        first.Output.ShouldContain("2026-10-15 legistar/guilford/1631 Board of Commissioners: agenda published, no item matches.");
        first.Output.ShouldContain("6 meeting(s), 1 agenda(s) retrieved; opened 1 issue(s), 0 agenda change(s); 0 error(s).");

        second.Code.ShouldBe(ExitCodes.Ok, second.Error);
        second.Output.ShouldContain("2026-10-15 legistar/guilford/1599 Board of Commissioners: unchanged since #1 (rules: data_center).");
        second.Output.ShouldContain("opened 0 issue(s)");

        var issue = _queue.Opened.ShouldHaveSingleItem();
        issue.Title.ShouldBe("Review: Guilford County, Board of Commissioners, 2026-10-15 (case 2026-439)");
        issue.Body.ShouldContain("Legistar also lists meeting 1631 for the same body, date and time");
        _documents.Writes.ShouldBe(1);
        _runLog.Runs[0].Documents.ShouldHaveSingleItem().Uri.Host.ShouldBe("guilford.legistar.com");
        _fetcher.Requests.ShouldNotContain(uri => uri.Host == "guilford.legistar1.com"); // robots.txt: Disallow: /
        _state.States.Count.ShouldBe(6);
        _runLog.Runs.Count.ShouldBe(2);
        _runLog.Runs[1].Documents.ShouldHaveSingleItem().Sha256.ShouldBe(_runLog.Runs[0].Documents.ShouldHaveSingleItem().Sha256);
        _composedDryRun.ShouldAllBe(dryRun => !dryRun);
    }

    [Fact]
    public async Task Output_and_issues_never_carry_source_titles()
    {
        var result = await IngestAsync();

        result.Output.ShouldNotContain("Synthetic agenda item");
        _queue.Opened.ShouldHaveSingleItem().Body.ShouldNotContain("Synthetic agenda item");
    }

    [Fact]
    public async Task Dry_run_composes_without_private_services_and_writes_nothing()
    {
        var result = await IngestAsync("--dry-run");

        result.Code.ShouldBe(ExitCodes.Ok, result.Error);
        _composedDryRun.ShouldHaveSingleItem().ShouldBeTrue();
        result.Output.ShouldStartWith("Dry run: nothing is stored and no issue is opened. guilford:");
        result.Output.ShouldContain("1599 Board of Commissioners: would open \"Review: Guilford County, Board of Commissioners, 2026-10-15 (case 2026-439)\" (rules: data_center).");
        result.Output.ShouldContain("not stored (dry run)");
        _queue.Opened.ShouldBeEmpty();
        _state.States.ShouldBeEmpty();
        _runLog.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Scheduled_run_logs_how_late_it_started()
    {
        var result = await IngestAsync("--schedule", "17 */6 * * *", "--run-id", "40623358875-1");

        result.Output.ShouldContain("run 40623358875-1, started 00:12:30 after its schedule.");
        _runLog.Runs.ShouldHaveSingleItem().StartDelay.ShouldBe(new TimeSpan(0, 12, 30));
    }

    [Fact]
    public async Task Run_id_defaults_to_the_local_time()
    {
        await IngestAsync();

        _runLog.Runs.ShouldHaveSingleItem().RunId.ShouldBe("local-20261028T122930Z");
    }

    [Fact]
    public async Task Source_failure_exits_3_with_a_workflow_annotation()
    {
        _source = new FakeSource
        {
            Failure = new SourceFailedException(SourceFailure.Unavailable, new Uri("https://webapi.legistar.com/v1/guilford/events"), "Legistar: HTTP 503\nafter retries."),
        };

        var result = await IngestAsync("--format", "github");

        result.Code.ShouldBe(ExitCodes.ExternalFailure);
        result.Output.ShouldContain("::error title=ingest guilford::Legistar: HTTP 503%0Aafter retries.");
        _runLog.Runs.ShouldHaveSingleItem().Outcome.ShouldBe(RunOutcome.Failed);
    }

    [Fact]
    public async Task Run_log_failure_is_a_warning_not_a_failure()
    {
        _runLog.Failure = new StorageFailedException("runs/x", "Couldn't write runs/x in the private bucket: HTTP 500");

        var result = await IngestAsync();

        result.Code.ShouldBe(ExitCodes.Ok);
        result.Error.ShouldContain("Warning: The run log wasn't written (the run still counts): Couldn't write runs/x");
    }

    [Theory]
    [InlineData("--jurisdiction", "../guilford")]
    [InlineData("--jurisdiction", "Guilford")]
    [InlineData("--run-id", "../runs")]
    [InlineData("--run-id", "a b")]
    public async Task Unsafe_arguments_are_usage_errors(string option, string value)
    {
        string[] args = option == "--jurisdiction"
            ? ["ingest", "--jurisdiction", value, "--data", DataDirectory()]
            : ["ingest", "--jurisdiction", "guilford", "--data", DataDirectory(), option, value];

        var result = await DclRunner.RunWithAsync(Root, args);

        result.Code.ShouldBe(ExitCodes.Usage);
        _composedDryRun.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_jurisdiction_is_a_usage_error()
    {
        var result = await DclRunner.RunWithAsync(Root, "ingest", "--jurisdiction", "winston-salem", "--data", DataDirectory());

        result.Code.ShouldBe(ExitCodes.Usage);
        result.Error.ShouldContain("No jurisdiction file jurisdictions/winston-salem.yaml");
    }

    [Fact]
    public async Task Manual_jurisdiction_is_a_usage_error()
    {
        var data = Path.Combine(_temp, "data");
        Directory.CreateDirectory(Path.Combine(data, "jurisdictions"));
        await File.WriteAllTextAsync(Path.Combine(data, "jurisdictions", "greensboro.yaml"), "id: greensboro\nname: Greensboro\nplatform: manual\n", TestContext.Current.CancellationToken);

        var result = await DclRunner.RunWithAsync(Root, "ingest", "--jurisdiction", "greensboro", "--data", data);

        result.Code.ShouldBe(ExitCodes.Usage);
        result.Error.ShouldContain("greensboro is entered by hand (platform: manual), so there is nothing to ingest.");
    }

    [Fact]
    public async Task Broken_jurisdiction_file_is_a_usage_error()
    {
        var data = Path.Combine(_temp, "data");
        Directory.CreateDirectory(Path.Combine(data, "jurisdictions"));
        await File.WriteAllTextAsync(Path.Combine(data, "jurisdictions", "guilford.yaml"), "id: guilford\nname: Guilford County\nplatform: legistar\nsecret_field: x\n", TestContext.Current.CancellationToken);

        var result = await DclRunner.RunWithAsync(Root, "ingest", "--jurisdiction", "guilford", "--data", data);

        result.Code.ShouldBe(ExitCodes.Usage);
        result.Error.ShouldContain("jurisdictions/guilford.yaml doesn't load: [UNKNOWN_FIELD]");
    }

    [Fact]
    public async Task Missing_configuration_exits_2_and_names_every_variable()
    {
        var result = await DclRunner.RunWithAsync(
            (output, error) => CliApp.CreateRootCommand(output, error, dryRun => IngestComposition.Build(_ => null, dryRun)),
            "ingest", "--jurisdiction", "guilford", "--data", DataDirectory());

        result.Code.ShouldBe(ExitCodes.Usage);
        result.Error.ShouldContain(
            "Configuration: Missing environment variables: R2_ACCOUNT_ID, R2_PRIVATE_BUCKET, R2_PRIVATE_ACCESS_KEY_ID, R2_PRIVATE_SECRET_ACCESS_KEY, GITHUB_REPOSITORY, GITHUB_TOKEN.");
    }

    [Fact]
    public async Task Unexpected_exception_exits_4()
    {
        var result = await DclRunner.RunWithAsync(
            (output, error) => CliApp.CreateRootCommand(output, error, _ => throw new InvalidOperationException("boom")),
            "ingest", "--jurisdiction", "guilford", "--data", DataDirectory());

        result.Code.ShouldBe(ExitCodes.Bug);
        result.Error.ShouldContain("dcl stopped on an unexpected error (exit code 4). Please report it: System.InvalidOperationException: boom");
    }

    public void Dispose()
    {
        Directory.Delete(_temp, recursive: true);
        GC.SuppressFinalize(this);
    }

    private Task<(int Code, string Output, string Error)> IngestAsync(params string[] options) =>
        DclRunner.RunWithAsync(Root, ["ingest", "--jurisdiction", "guilford", "--data", DataDirectory(), .. options]);

    private System.CommandLine.RootCommand Root(TextWriter output, TextWriter error) => CliApp.CreateRootCommand(output, error, Compose);

    private ServiceProvider Compose(bool dryRun)
    {
        _composedDryRun.Add(dryRun);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<ISourceAdapter>(_source ?? (ISourceAdapter)new LegistarAdapter(_fetcher));
        services.AddSingleton<IDocumentStore>(_documents);
        services.AddSingleton<IIngestState>(_state);
        services.AddSingleton<IReviewQueue>(_queue);
        services.AddSingleton<IRunLog>(_runLog);
        services.AddTransient<IngestJurisdiction>();
        return services.BuildServiceProvider();
    }

    private static string DataDirectory() => DclRunner.RepositoryPath("data");

    /// <summary>
    /// Legistar's recorded API answers, plus meeting pages that link their agenda the way InSite does and agendas that
    /// are a few PDF bytes named after the meeting. Nothing on guilford.legistar1.com is served: its robots.txt forbids it.
    /// </summary>
    private static (byte[] Content, string? MediaType)? RecordedGuilford(Uri uri)
    {
        if (uri.Host == "guilford.legistar.com")
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return uri.AbsolutePath switch
            {
                "/MeetingDetail.aspx" => (Encoding.UTF8.GetBytes(
                    $"<a href=\"View.ashx?M=A&amp;ID=13{query["LEGID"]}&amp;GUID=50757AC6-9ADB-4840-A8C9-09E7E8BAD1A3&amp;G=CC3BAA2D-CF4F-4E63-9ADD-2F9E8DCCB38C\">Agenda</a>"),
                    "text/html"),
                "/View.ashx" => (Encoding.UTF8.GetBytes($"%PDF-1.7 agenda {query["ID"]}"), "application/pdf"),
                _ => null,
            };
        }

        var segments = uri.AbsolutePath.Split('/');
        var file = segments[^1] switch
        {
            "events" => "events.json",
            "eventitems" => $"eventitems-{segments[^2]}.json",
            _ => null,
        };
        var path = file is null ? null : DclRunner.RepositoryPath("tests", "fixtures", "legistar", "guilford", file);
        return uri.Host == "webapi.legistar.com" && path is not null && File.Exists(path) ? (File.ReadAllBytes(path), "application/json") : null;
    }
}
