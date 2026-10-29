using System.CommandLine;
using System.Globalization;
using Xunit;

namespace DcLedger.Cli.Tests;

/// <summary>Runs dcl in-process with captured output, and finds files in the repository.</summary>
internal static class DclRunner
{
    /// <summary>Runs dcl with the real command tree.</summary>
    public static Task<(int Code, string Output, string Error)> RunAsync(params string[] args) =>
        RunWithAsync(CliApp.CreateRootCommand, args);

    /// <summary>Runs dcl with the command tree <paramref name="root"/> builds for the captured streams.</summary>
    public static async Task<(int Code, string Output, string Error)> RunWithAsync(Func<TextWriter, TextWriter, RootCommand> root, params string[] args)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var code = await CliApp.RunAsync(root(output, error), args, output, error, TestContext.Current.CancellationToken);
        return (code, output.ToString(), error.ToString());
    }

    /// <summary>A path under the repository root (the directory holding DcLedger.slnx).</summary>
    public static string RepositoryPath(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DcLedger.slnx")))
            {
                return Path.Combine([directory.FullName, .. parts]);
            }
        }

        throw new InvalidOperationException("DcLedger.slnx not found above " + AppContext.BaseDirectory);
    }
}
