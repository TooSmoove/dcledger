using System.CommandLine;
using System.Globalization;
using System.Text.RegularExpressions;
using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Infrastructure.Ledger;
using Microsoft.Extensions.DependencyInjection;

namespace DcLedger.Cli;

/// <summary>
/// <c>dcl ingest</c> (issue #19, ADR-0004): one jurisdiction per run. Exit 0 when everything was checked, 2 for bad
/// arguments or configuration, 3 when the source, the private bucket or GitHub failed (the jurisdiction is "not
/// checked", ADR-0005). Output names meetings by ID, date and body only, never by source titles, because Actions logs
/// are public.
/// </summary>
internal static partial class IngestCommand
{
    private const string Utc = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>Creates the command. <paramref name="compose"/> builds the services for a real run (false) or a dry run (true).</summary>
    internal static Command Create(TextWriter output, TextWriter error, Func<bool, ServiceProvider> compose)
    {
        var jurisdiction = new Option<string>("--jurisdiction")
        {
            Description = "The jurisdiction's ID, for example guilford. Its file in the data directory says where and what to read.",
            Required = true,
        };
        var data = new Option<string>("--data")
        {
            Description = "The ledger data directory.",
            DefaultValueFactory = _ => "data",
        };
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Read the source and download matching agendas, but store nothing and open no issues. Needs no credentials.",
        };
        var runId = new Option<string?>("--run-id")
        {
            Description = "Names this run in the run log, for example the Actions run ID and attempt (40623358875-1). Default: local-<UTC time>.",
        };
        var schedule = new Option<string?>("--schedule")
        {
            Description = "The cron schedule that started the run, to log how late it started (SPEC AC9).",
        };
        var format = new Option<string>("--format")
        {
            Description = "Output format: text, or github for workflow annotations.",
            DefaultValueFactory = _ => "text",
        };
        format.AcceptOnlyFromAmong("text", "github");

        var command = new Command("ingest", "Read a jurisdiction's agenda platform, keep matching agendas and open review issues.")
        {
            jurisdiction,
            data,
            dryRun,
            runId,
            schedule,
            format,
        };
        command.SetAction((parseResult, cancellationToken) => RunAsync(
            new Arguments(
                parseResult.GetValue(jurisdiction)!,
                parseResult.GetValue(data)!,
                parseResult.GetValue(dryRun),
                parseResult.GetValue(runId),
                parseResult.GetValue(schedule),
                parseResult.GetValue(format) == "github"),
            new Streams(output, error),
            compose,
            cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(Arguments arguments, Streams console, Func<bool, ServiceProvider> compose, CancellationToken cancellationToken)
    {
        if (!Slug().IsMatch(arguments.JurisdictionId))
        {
            return console.Usage("--jurisdiction must be lowercase letters, digits and single hyphens.");
        }

        if (arguments.RunId is { } explicitRunId && !SafeRunId().IsMatch(explicitRunId))
        {
            return console.Usage("--run-id must be letters, digits, '.', '_' or '-', at most 128 characters.");
        }

        if (LoadJurisdiction(arguments, console) is not { } jurisdiction)
        {
            return ExitCodes.Usage;
        }

        ServiceProvider services;
        try
        {
            services = compose(arguments.DryRun);
        }
        catch (ArgumentException exception)
        {
            return console.Usage($"Configuration: {exception.Message}");
        }

        await using (services.ConfigureAwait(false))
        {
            var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
            var runId = arguments.RunId ?? $"local-{now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}";
            var request = new IngestRequest
            {
                Jurisdiction = jurisdiction,
                RunId = runId,
                ScheduledForUtc = CronSlot.Previous(arguments.Schedule, now),
                DryRun = arguments.DryRun,
            };
            var report = await services.GetRequiredService<IngestJurisdiction>().RunAsync(request, cancellationToken).ConfigureAwait(false);
            Print(report, runId, arguments.Github, console);
            return report.Succeeded ? ExitCodes.Ok : ExitCodes.ExternalFailure;
        }
    }

    private static Jurisdiction? LoadJurisdiction(Arguments arguments, Streams console)
    {
        if (!Directory.Exists(arguments.DataDirectory))
        {
            console.Error.WriteLine($"Data directory not found: {arguments.DataDirectory}");
            return null;
        }

        var file = $"jurisdictions/{arguments.JurisdictionId}.yaml";
        var snapshot = new YamlLedgerReader().Load(arguments.DataDirectory);
        var problems = snapshot.LoadErrors.Where(problem => problem.File == file).ToList();
        if (problems.Count > 0)
        {
            console.Error.WriteLine($"{file} doesn't load: {string.Join(" ", problems.Select(problem => $"[{problem.Rule}] {problem.Message}"))} Run dcl validate.");
            return null;
        }

        var jurisdiction = snapshot.Jurisdictions.FirstOrDefault(loaded => loaded.File == file)?.Record;
        if (jurisdiction is null)
        {
            console.Error.WriteLine($"No jurisdiction file {file} in {arguments.DataDirectory}.");
            return null;
        }

        if (jurisdiction.Platform == Platform.Manual)
        {
            console.Error.WriteLine($"{jurisdiction.Id} is entered by hand (platform: manual), so there is nothing to ingest.");
            return null;
        }

        return jurisdiction;
    }

    private static void Print(IngestReport report, string runId, bool github, Streams console)
    {
        var delay = report.StartDelay is { } late ? $", started {late.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)} after its schedule" : string.Empty;
        var mode = report.DryRun ? "Dry run: nothing is stored and no issue is opened. " : string.Empty;
        console.Out.WriteLine($"{mode}{report.JurisdictionId}: meetings dated {Day(report.FirstDate)} to {Day(report.LastDate)}, run {runId}{delay}.");

        foreach (var meeting in report.Meetings)
        {
            var key = meeting.Meeting;
            console.Out.WriteLine($"{Day(meeting.Date)} {key.Platform.ToToken()}/{key.Client}/{key.MeetingId} {meeting.Body}: {Describe(meeting)}");
        }

        foreach (var document in report.Documents)
        {
            var stored = report.DryRun ? "not stored (dry run)" : document.Created ? "stored" : "already stored";
            console.Out.WriteLine(
                $"Retrieved {document.Uri.AbsoluteUri} at {document.RetrievedAtUtc.UtcDateTime.ToString(Utc, CultureInfo.InvariantCulture)}: {document.Bytes} bytes, sha256 {document.Sha256.Value}, {stored}.");
        }

        var opened = report.Meetings.Count(meeting => meeting.Outcome is MeetingOutcome.IssueOpened or MeetingOutcome.WouldOpenIssue);
        var changed = report.Meetings.Count(meeting => meeting.Outcome == MeetingOutcome.AgendaChanged);
        var verb = report.DryRun ? "would open" : "opened";
        console.Out.WriteLine(
            $"{report.Meetings.Count} meeting(s), {report.Documents.Count} agenda(s) retrieved; {verb} {opened} issue(s), {changed} agenda change(s); {report.Errors.Count} error(s).");

        var title = $"ingest {report.JurisdictionId}";
        foreach (var problem in report.Errors)
        {
            if (github)
            {
                console.Out.WriteLine(WorkflowCommands.Error(title, problem));
            }
            else
            {
                console.Error.WriteLine($"Error: {problem}");
            }
        }

        if (report.RunLogError is { } runLogError)
        {
            var message = $"The run log wasn't written (the run still counts): {runLogError}";
            if (github)
            {
                console.Out.WriteLine(WorkflowCommands.Warning(title, message));
            }
            else
            {
                console.Error.WriteLine($"Warning: {message}");
            }
        }
    }

    private static string Describe(MeetingResult meeting)
    {
        var rules = meeting.Rules.Count > 0 ? $" (rules: {string.Join(", ", meeting.Rules)})" : string.Empty;
        var issue = meeting.IssueNumber is { } number ? $"#{number.ToString(CultureInfo.InvariantCulture)}" : "the issue";
        return meeting.Outcome switch
        {
            MeetingOutcome.NoAgenda => "no agenda published yet.",
            MeetingOutcome.NoMatch => "agenda published, no item matches.",
            MeetingOutcome.IssueOpened => $"opened issue {issue}{rules}.",
            MeetingOutcome.WouldOpenIssue => $"would open \"{meeting.IssueTitle}\"{rules}.",
            MeetingOutcome.AgendaChanged => $"the agenda changed; commented on {issue}{(meeting.Reopened ? " and reopened it" : string.Empty)}{rules}.",
            MeetingOutcome.Unchanged => $"unchanged since {issue}{rules}.",
            MeetingOutcome.Failed => $"failed: {meeting.Error}",
            _ => meeting.Outcome.ToString(),
        };
    }

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The validator's ID rule (lowercase words joined by single hyphens) and the storage key rule for run IDs.
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Slug();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeRunId();

    private sealed record Arguments(string JurisdictionId, string DataDirectory, bool DryRun, string? RunId, string? Schedule, bool Github);

    /// <summary>The command's two output streams.</summary>
    private sealed record Streams(TextWriter Out, TextWriter Error)
    {
        public int Usage(string message)
        {
            Error.WriteLine(message);
            return ExitCodes.Usage;
        }
    }
}
