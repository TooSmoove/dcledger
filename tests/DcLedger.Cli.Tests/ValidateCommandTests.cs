using System.Globalization;
using Shouldly;
using Xunit;

namespace DcLedger.Cli.Tests;

public sealed class ValidateCommandTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("dcl-validate-").FullName;

    [Fact]
    public void Valid_ledger_exits_ok()
    {
        var (code, output, _) = Run("validate", "--data", FixturePath("valid"));

        code.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("Checked 4 record file(s)");
        output.ShouldContain("0 problem(s)");
    }

    [Fact]
    public void Invalid_ledger_exits_1_and_names_file_rule_and_record()
    {
        var data = WriteEvent("id: fixture-boc\noriginal_title: Someone's petition\n");

        var (code, output, _) = Run("validate", "--data", data);

        code.ShouldBe(ExitCodes.DataInvalid);
        output.ShouldContain($"{data.Replace('\\', '/')}/events/fixture-boc.yaml: [FORBIDDEN_FIELD] fixture-boc: 'original_title' must not be stored");
    }

    [Fact]
    public void Github_format_emits_workflow_annotations()
    {
        var data = WriteEvent("id: fixture-boc\n");

        var (code, output, _) = Run("validate", "--data", data, "--format", "github");

        code.ShouldBe(ExitCodes.DataInvalid);
        output.ShouldContain("::error file=");
        output.ShouldContain(",title=REQUIRED_FIELD::fixture-boc: 'jurisdiction_id' is required.");
    }

    [Fact]
    public void Removed_record_listed_in_previous_files_fails()
    {
        var previous = Path.Combine(_temp, "previous.txt");
        string[] lines = ["events/was-published.yaml", "README.md"];
        File.WriteAllLines(previous, lines);

        var (code, output, _) = Run("validate", "--data", FixturePath("valid"), "--previous-files", previous);

        code.ShouldBe(ExitCodes.DataInvalid);
        output.ShouldContain("events/was-published.yaml: [SLUG_CHANGED]");
        output.ShouldContain("1 problem(s)");
    }

    [Fact]
    public void Missing_data_directory_is_a_usage_error()
    {
        var (code, _, error) = Run("validate", "--data", Path.Combine(_temp, "nowhere"));

        code.ShouldBe(ExitCodes.Usage);
        error.ShouldContain("Data directory not found");
    }

    [Fact]
    public void Unknown_format_is_a_usage_error()
    {
        var (code, _, _) = Run("validate", "--data", FixturePath("valid"), "--format", "xml");

        code.ShouldBe(ExitCodes.Usage);
    }

    public void Dispose()
    {
        Directory.Delete(_temp, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string WriteEvent(string yaml)
    {
        var data = Path.Combine(_temp, "data");
        Directory.CreateDirectory(Path.Combine(data, "events"));
        File.WriteAllText(Path.Combine(data, "events", "fixture-boc.yaml"), yaml);
        return data;
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var code = CliApp.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private static string FixturePath(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DcLedger.slnx")))
            {
                return Path.Combine(directory.FullName, "tests", "fixtures", "ledger", name);
            }
        }

        throw new InvalidOperationException("DcLedger.slnx not found above " + AppContext.BaseDirectory);
    }
}
