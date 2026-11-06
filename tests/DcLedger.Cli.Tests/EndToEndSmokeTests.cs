using DcLedger.Adapters.Legistar;
using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace DcLedger.Cli.Tests;

/// <summary>
/// The permanent end-to-end smoke test (issue #24, ADR-0006; WS-AC1, WS-AC2, WS-AC4). Recorded Legistar responses go
/// through <c>dcl ingest</c> (with in-memory stand-ins for the private bucket and the review queue), then the review
/// issue's YAML is pasted the way a reviewer would, then <c>dcl validate</c> and <c>dcl export</c> run. It runs offline
/// on both operating systems in the dotnet job. The e2e job in ci also sets <see cref="OutputVariable"/>, builds the site
/// from that export and checks /radar with <c>site/e2e/smoke.mjs</c>.
/// </summary>
public sealed class EndToEndSmokeTests : IDisposable
{
    /// <summary>When set, the export is written there (ci's e2e job uses the repository's <c>out/</c>, where the site reads it).</summary>
    public const string OutputVariable = "DCL_E2E_OUT";

    // The ID the review issue proposes and the title the "reviewer" writes. site/e2e/smoke.mjs looks for both.
    private const string EventId = "guilford-board-of-commissioners-2026-10-15";
    private const string ReviewedTitle = "Public hearing on a data center rezoning (end-to-end test)";

    // The recorded hearing (meeting 1599) is on Oct 15, 2026 at 5:30 PM EDT. Ingest runs five days earlier, so the
    // export that follows puts it inside the radar's 30-day window.
    private static readonly DateTimeOffset FirstRun = new(2026, 10, 10, 12, 17, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondRun = FirstRun.AddHours(6);

    private readonly string _temp = Directory.CreateTempSubdirectory("dcl-e2e-").FullName;
    private readonly FixedTime _time = new(FirstRun);
    private readonly FakeFetcher _fetcher;
    private readonly MemoryDocumentStore _documents = new();
    private readonly MemoryIngestState _state = new();
    private readonly MemoryReviewQueue _queue = new();
    private readonly MemoryRunLog _runLog = new();

    public EndToEndSmokeTests()
    {
        _fetcher = new FakeFetcher(_time) { Fallback = RecordedLegistar.Respond };
    }

    [Fact]
    public async Task Recorded_agenda_reaches_the_calendar_and_csv_and_a_rerun_opens_no_issue()
    {
        var data = LedgerWithGuilford();

        // Ingest: one matching meeting, one review issue.
        var first = await IngestAsync(data, "e2e-1");
        first.Code.ShouldBe(ExitCodes.Ok, first.Error);
        var issue = _queue.Opened.ShouldHaveSingleItem();
        var agenda = _documents.Documents.Keys.ShouldHaveSingleItem().Value;

        // Review: paste both offered files and write a neutral title, then the quality gate.
        PasteOfferedFiles(issue.Body, data).ShouldBe([$"data/documents/{agenda}.yaml", $"data/events/{EventId}.yaml"], ignoreOrder: true);
        var validate = await DclRunner.RunAsync("validate", "--data", data);
        validate.Code.ShouldBe(ExitCodes.Ok, validate.Output + validate.Error);

        // A rerun six hours later (the ingest cadence) finds the same agenda and opens nothing (WS-AC2).
        _time.Now = SecondRun;
        var second = await IngestAsync(data, "e2e-2");
        second.Code.ShouldBe(ExitCodes.Ok, second.Error);
        second.Output.ShouldContain("opened 0 issue(s), 0 agenda change(s), 0 meeting change(s); 0 error(s).");
        _queue.Opened.Count.ShouldBe(1);
        _queue.Comments.ShouldBeEmpty();
        _runLog.Runs[1].Documents.ShouldHaveSingleItem().Sha256.Value.ShouldBe(agenda);

        // Export, as publish does right after an ingest.
        var output = OutputDirectory();
        var export = await DclRunner.RunAsync("export", "--data", data, "--status", StatusFile(), "--out", output);
        export.Code.ShouldBe(ExitCodes.Ok, export.Output + export.Error);

        var uid = EventUid.Create("guilford", Platform.Legistar, "1599");
        foreach (var calendar in new[] { "radar.ics", Path.Combine("radar", "guilford.ics") })
        {
            var vevent = SingleEvent(File.ReadAllText(Path.Combine(output, "public", calendar)));
            vevent.ShouldContain($"\r\nUID:{uid}\r\n", Case.Sensitive);
            vevent.ShouldContain("\r\nDTSTART;TZID=America/New_York:20261015T173000\r\n", Case.Sensitive);
        }

        var rows = File.ReadAllLines(Path.Combine(output, "public", "events.csv"));
        rows.Length.ShouldBe(2); // the header and the event
        rows[1].ShouldStartWith(
            $"{EventId},{uid},guilford,Board of Commissioners,{ReviewedTitle},2026-439,2026-10-15T21:30:00Z,2026-10-15T17:30:00-04:00,scheduled,0,confirmed_official,");
        rows[1].ShouldEndWith($",{agenda}");
    }

    public void Dispose()
    {
        Directory.Delete(_temp, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>A fresh ledger holding only the repository's own Guilford configuration, so the test follows it.</summary>
    private string LedgerWithGuilford()
    {
        var data = Path.Combine(_temp, "data");
        Directory.CreateDirectory(Path.Combine(data, "jurisdictions"));
        File.Copy(DclRunner.RepositoryPath("data", "jurisdictions", "guilford.yaml"), Path.Combine(data, "jurisdictions", "guilford.yaml"));
        return data;
    }

    /// <summary>Writes the issue's files into <paramref name="data"/>, with a title in the event, and returns their paths.</summary>
    private static List<string> PasteOfferedFiles(string issueBody, string data)
    {
        var files = OfferedFiles.Parse(issueBody);
        foreach (var (path, yaml) in files)
        {
            var target = Path.Combine(data, path["data/".Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, path.StartsWith("data/events/", StringComparison.Ordinal) ? OfferedFiles.WithTitle(yaml, ReviewedTitle) : yaml);
        }

        return [.. files.Keys];
    }

    /// <summary>The status publish would build: Guilford checked by the second run, generated a few minutes later.</summary>
    private string StatusFile()
    {
        var path = Path.Combine(_temp, "ingest-status.json");
        File.WriteAllText(path, """
            {
              "schema_version": 1,
              "generated_utc": "2026-10-10T18:22:00Z",
              "jurisdictions": [{ "id": "guilford", "last_checked_utc": "2026-10-10T18:17:00Z" }]
            }
            """);
        return path;
    }

    private string OutputDirectory() =>
        Environment.GetEnvironmentVariable(OutputVariable) is { Length: > 0 } shared ? Path.GetFullPath(shared) : Path.Combine(_temp, "out");

    /// <summary>The calendar's only VEVENT, with folded lines unfolded (RFC 5545 §3.1).</summary>
    private static string SingleEvent(string calendar)
    {
        var unfolded = calendar.Replace("\r\n ", string.Empty, StringComparison.Ordinal);
        var events = unfolded.Split("\r\nBEGIN:VEVENT\r\n")[1..];
        return "\r\n" + events.ShouldHaveSingleItem();
    }

    private Task<(int Code, string Output, string Error)> IngestAsync(string data, string runId) =>
        DclRunner.RunWithAsync(
            (output, error) => CliApp.CreateRootCommand(output, error, Compose),
            "ingest", "--jurisdiction", "guilford", "--data", data, "--run-id", runId);

    private ServiceProvider Compose(bool dryRun)
    {
        dryRun.ShouldBeFalse();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<ISourceAdapter>(new LegistarAdapter(_fetcher));
        services.AddSingleton<IDocumentStore>(_documents);
        services.AddSingleton<IIngestState>(_state);
        services.AddSingleton<IReviewQueue>(_queue);
        services.AddSingleton<IRunLog>(_runLog);
        services.AddTransient<IngestJurisdiction>();
        return services.BuildServiceProvider();
    }
}
