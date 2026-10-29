using Shouldly;
using Xunit;

namespace DcLedger.Cli.Tests;

public sealed class CliAppTests
{
    [Fact]
    public async Task Help_describes_the_tool_and_exits_ok()
    {
        var (code, output, _) = await DclRunner.RunAsync("--help");

        code.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("dc-ledger");
    }

    [Fact]
    public async Task Version_exits_ok_and_prints_a_version()
    {
        var (code, output, _) = await DclRunner.RunAsync("--version");

        code.ShouldBe(ExitCodes.Ok);
        output.Trim().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Unknown_option_is_a_usage_error_with_exit_code_2()
    {
        var (code, _, error) = await DclRunner.RunAsync("--no-such-option");

        code.ShouldBe(ExitCodes.Usage);
        error.ShouldContain("--no-such-option");
    }
}
