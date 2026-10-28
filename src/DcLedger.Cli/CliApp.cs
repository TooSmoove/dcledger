using System.CommandLine;

namespace DcLedger.Cli;

/// <summary>Builds the dcl command tree and runs it with explicit output streams so it can be tested in-process.</summary>
internal static class CliApp
{
    internal const string Description =
        "dc-ledger: source-linked public-record ledger and Hearing Radar for NC Triad data-center decisions.";

    internal static RootCommand CreateRootCommand(TextWriter output, TextWriter error) =>
        new(Description) { ValidateCommand.Create(output, error) };

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var parseResult = CreateRootCommand(output, error).Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            foreach (var parseError in parseResult.Errors)
            {
                error.WriteLine(parseError.Message);
            }

            return ExitCodes.Usage;
        }

        return parseResult.Invoke(new InvocationConfiguration { Output = output, Error = error });
    }
}
