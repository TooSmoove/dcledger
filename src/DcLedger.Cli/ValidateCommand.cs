using System.CommandLine;
using DcLedger.Application;
using DcLedger.Infrastructure.Ledger;

namespace DcLedger.Cli;

/// <summary><c>dcl validate</c>: the quality gate over the ledger data (WS-AC5). Exit 0 valid, 1 invalid, 2 usage.</summary>
internal static class ValidateCommand
{
    internal static Command Create(TextWriter output, TextWriter error)
    {
        var data = new Option<string>("--data")
        {
            Description = "The ledger data directory (usually data).",
            Required = true,
        };
        var previous = new Option<string?>("--previous-files")
        {
            Description = "A file listing record files (relative to the data directory) that existed on the base branch; each must still exist.",
        };
        var format = new Option<string>("--format")
        {
            Description = "Output format: text, or github for workflow annotations.",
            DefaultValueFactory = _ => "text",
        };
        format.AcceptOnlyFromAmong("text", "github");

        var command = new Command("validate", "Check the ledger data against the quality gate.") { data, previous, format };
        command.SetAction(parseResult => Run(
            parseResult.GetValue(data)!,
            parseResult.GetValue(previous),
            parseResult.GetValue(format) == "github",
            output,
            error));
        return command;
    }

    private static int Run(string dataDirectory, string? previousFilesPath, bool github, TextWriter output, TextWriter error)
    {
        if (!Directory.Exists(dataDirectory))
        {
            error.WriteLine($"Data directory not found: {dataDirectory}");
            return ExitCodes.Usage;
        }

        if (previousFilesPath is not null && !File.Exists(previousFilesPath))
        {
            error.WriteLine($"Previous-files list not found: {previousFilesPath}");
            return ExitCodes.Usage;
        }

        IReadOnlyCollection<string> previousFiles = previousFilesPath is null
            ? []
            : File.ReadAllLines(previousFilesPath).Select(line => line.Trim()).Where(YamlLedgerReader.IsRecordPath).ToList();

        var report = new ValidateLedger(new YamlLedgerReader()).Run(dataDirectory, previousFiles);
        LedgerProblems.Print(report.Errors, dataDirectory, github, output);
        output.WriteLine($"Checked {report.RecordFiles} record file(s) in {dataDirectory}: {report.Errors.Count} problem(s).");
        return report.IsValid ? ExitCodes.Ok : ExitCodes.DataInvalid;
    }
}
