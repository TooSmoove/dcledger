using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace DcLedger.Cli;

/// <summary>Builds the dcl command tree and runs it with explicit output streams so it can be tested in-process.</summary>
internal static class CliApp
{
    internal const string Description =
        "dc-ledger: source-linked public-record ledger and Hearing Radar for NC Triad data-center decisions.";

    /// <summary>The command tree with the real services, configured from the environment.</summary>
    internal static RootCommand CreateRootCommand(TextWriter output, TextWriter error) =>
        CreateRootCommand(output, error, dryRun => IngestComposition.Build(Environment.GetEnvironmentVariable, dryRun));

    /// <summary>The command tree; <paramref name="composeIngest"/> builds ingest's services (tests pass fakes).</summary>
    internal static RootCommand CreateRootCommand(TextWriter output, TextWriter error, Func<bool, ServiceProvider> composeIngest) =>
        new(Description)
        {
            ValidateCommand.Create(output, error),
            IngestCommand.Create(output, error, composeIngest),
            ExportCommand.Create(output, error),
        };

    /// <summary>Runs dcl with the real services.</summary>
    internal static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
        RunAsync(CreateRootCommand(output, error), args, output, error, cancellationToken);

    /// <summary>
    /// Parses and runs <paramref name="args"/>. Exit codes follow ADR-0004: parse errors are 2, and an unexpected
    /// exception (a bug) is 4 with its details on the error stream.
    /// </summary>
    internal static async Task<int> RunAsync(RootCommand root, string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var parseResult = root.Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            foreach (var parseError in parseResult.Errors)
            {
                error.WriteLine(parseError.Message);
            }

            return ExitCodes.Usage;
        }

        var configuration = new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false };
        try
        {
            return await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            error.WriteLine($"dcl stopped on an unexpected error (exit code {ExitCodes.Bug}). Please report it: {exception}");
            return ExitCodes.Bug;
        }
    }
}
