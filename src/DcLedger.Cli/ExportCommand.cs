using System.CommandLine;
using DcLedger.Application;
using DcLedger.Infrastructure.Export;
using DcLedger.Infrastructure.Ledger;
using DcLedger.Publishing.Ics;
using DcLedger.Publishing.SiteData;
using DcLedger.Publishing.Tabular;

namespace DcLedger.Cli;

/// <summary>
/// <c>dcl export --data &lt;dir&gt; --status &lt;file&gt; --out &lt;dir&gt;</c> (issue #20, ADR-0004): the site data, calendars
/// and CSV from the validated ledger. Exit 0 when the files were written, 1 when the ledger fails the quality gate
/// (nothing is written), 2 for bad arguments or an unusable ingest status.
/// </summary>
internal static class ExportCommand
{
    /// <summary>Every exporter, in the order they run. Their files are written in path order.</summary>
    private static readonly IExporter[] Exporters = [new SiteDataExporter(), new RadarCalendarExporter(), new TabularExporter()];

    internal static Command Create(TextWriter output, TextWriter error)
    {
        var data = new Option<string>("--data")
        {
            Description = "The ledger data directory (usually data).",
            Required = true,
        };
        var status = new Option<string>("--status")
        {
            Description = "ingest-status.json: when each automated jurisdiction was last checked. Publish builds it from ingest run history.",
            Required = true,
        };
        var outDirectory = new Option<string>("--out")
        {
            Description = "Output directory: site-data/ for the site build, public/ for files served as they are. Files are replaced; nothing is deleted.",
            Required = true,
        };
        var format = new Option<string>("--format")
        {
            Description = "Output format: text, or github for workflow annotations.",
            DefaultValueFactory = _ => "text",
        };
        format.AcceptOnlyFromAmong("text", "github");

        var command = new Command("export", "Write the site data, calendars and CSV from the validated ledger.") { data, status, outDirectory, format };
        command.SetAction(parseResult => Run(
            parseResult.GetValue(data)!,
            parseResult.GetValue(status)!,
            parseResult.GetValue(outDirectory)!,
            parseResult.GetValue(format) == "github",
            output,
            error));
        return command;
    }

    private static int Run(string dataDirectory, string statusFile, string outDirectory, bool github, TextWriter output, TextWriter error)
    {
        if (!Directory.Exists(dataDirectory))
        {
            error.WriteLine($"Data directory not found: {dataDirectory}");
            return ExitCodes.Usage;
        }

        IngestStatus status;
        try
        {
            status = IngestStatusFile.Read(statusFile);
        }
        catch (IngestStatusException exception)
        {
            error.WriteLine(exception.Message);
            return ExitCodes.Usage;
        }

        var report = new ExportLedger(new YamlLedgerReader(), Exporters, new DirectoryExportSink(outDirectory)).Run(dataDirectory, status);
        if (!report.Exported)
        {
            LedgerProblems.Print(report.Validation.Errors, dataDirectory, github, output);
            output.WriteLine($"{report.Validation.Errors.Count} problem(s) in {dataDirectory}; nothing was exported (run dcl validate).");
            return ExitCodes.DataInvalid;
        }

        foreach (var warning in report.Warnings)
        {
            if (github)
            {
                output.WriteLine(WorkflowCommands.Warning("dcl export", warning));
            }
            else
            {
                error.WriteLine($"Warning: {warning}");
            }
        }

        output.WriteLine($"Exported {report.Events} event(s) for {report.Jurisdictions} jurisdiction(s) to {outDirectory}: {string.Join(", ", report.Files)}.");
        return ExitCodes.Ok;
    }
}
