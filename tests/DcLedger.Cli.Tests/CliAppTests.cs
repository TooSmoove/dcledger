using System.Globalization;
using Shouldly;
using Xunit;

namespace DcLedger.Cli.Tests;

public sealed class CliAppTests
{
    [Fact]
    public void Help_describes_the_tool_and_exits_ok()
    {
        var (code, output, _) = Run("--help");

        code.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("dc-ledger");
    }

    [Fact]
    public void Version_exits_ok_and_prints_a_version()
    {
        var (code, output, _) = Run("--version");

        code.ShouldBe(ExitCodes.Ok);
        output.Trim().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Unknown_option_is_a_usage_error_with_exit_code_2()
    {
        var (code, _, error) = Run("--no-such-option");

        code.ShouldBe(ExitCodes.Usage);
        error.ShouldContain("--no-such-option");
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var code = CliApp.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }
}
