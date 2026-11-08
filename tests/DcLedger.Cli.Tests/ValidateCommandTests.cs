using Shouldly;
using Xunit;

namespace DcLedger.Cli.Tests;

public sealed class ValidateCommandTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("dcl-validate-").FullName;

    [Fact]
    public async Task Valid_ledger_exits_ok()
    {
        var (code, output, _) = await DclRunner.RunAsync("validate", "--data", FixturePath("valid"));

        code.ShouldBe(ExitCodes.Ok);
        output.ShouldContain("Checked 4 record file(s)");
        output.ShouldContain("0 problem(s)");
    }

    [Fact]
    public async Task Invalid_ledger_exits_1_and_names_file_rule_and_record()
    {
        var data = WriteEvent("id: fixture-boc\noriginal_title: Someone's petition\n");

        var (code, output, _) = await DclRunner.RunAsync("validate", "--data", data);

        code.ShouldBe(ExitCodes.DataInvalid);
        output.ShouldContain($"{data.Replace('\\', '/')}/events/fixture-boc.yaml: [FORBIDDEN_FIELD] fixture-boc: 'original_title' must not be stored");
    }

    [Fact]
    public async Task Github_format_emits_workflow_annotations()
    {
        var data = WriteEvent("id: fixture-boc\n");

        var (code, output, _) = await DclRunner.RunAsync("validate", "--data", data, "--format", "github");

        code.ShouldBe(ExitCodes.DataInvalid);
        output.ShouldContain("::error file=");
        output.ShouldContain(",title=REQUIRED_FIELD::fixture-boc: 'jurisdiction_id' is required.");
    }

    [Fact]
    public async Task Removed_record_listed_in_previous_files_fails()
    {
        var previous = Path.Combine(_temp, "previous.txt");
        string[] lines = ["events/was-published.yaml", "README.md"];
        File.WriteAllLines(previous, lines);

        var (code, output, _) = await DclRunner.RunAsync("validate", "--data", FixturePath("valid"), "--previous-files", previous);

        code.ShouldBe(ExitCodes.DataInvalid);
        output.ShouldContain("events/was-published.yaml: [SLUG_CHANGED]");
        output.ShouldContain("1 problem(s)");
    }

    [Fact]
    public async Task Cancelling_a_published_event_needs_a_raised_sequence()
    {
        var previous = CopyFixture("previous");
        var current = CopyFixture("current");
        var eventFile = Path.Combine(current, "events", "fixture-county-boc-2026-11-05.yaml");
        File.WriteAllText(eventFile, File.ReadAllText(eventFile).Replace("status: scheduled", "status: cancelled", StringComparison.Ordinal));

        var (code, output, _) = await DclRunner.RunAsync("validate", "--data", current, "--previous-data", previous);

        code.ShouldBe(ExitCodes.DataInvalid);
        output.ShouldContain("events/fixture-county-boc-2026-11-05.yaml: [SEQUENCE_NOT_RAISED] fixture-county-boc-2026-11-05: status changed but sequence is still 0.");

        File.WriteAllText(eventFile, File.ReadAllText(eventFile).Replace("sequence: 0", "sequence: 1", StringComparison.Ordinal));
        var (fixedCode, fixedOutput, _) = await DclRunner.RunAsync("validate", "--data", current, "--previous-data", previous);

        fixedCode.ShouldBe(ExitCodes.Ok, fixedOutput);
    }

    [Fact]
    public async Task Changing_a_published_decision_needs_a_raised_sequence()
    {
        var previous = CopyFixture("previous");
        var current = CopyFixture("current");
        var actionFile = Path.Combine(current, "actions", "fixture-county-action-2026-01-15.yaml");
        File.WriteAllText(actionFile, File.ReadAllText(actionFile).Replace("votes: 5-2", "votes: 6-1", StringComparison.Ordinal));

        var (code, output, _) = await DclRunner.RunAsync("validate", "--data", current, "--previous-data", previous);

        code.ShouldBe(ExitCodes.DataInvalid);
        output.ShouldContain("actions/fixture-county-action-2026-01-15.yaml: [SEQUENCE_NOT_RAISED] fixture-county-action-2026-01-15: votes changed but sequence is still 0.");

        File.WriteAllText(actionFile, File.ReadAllText(actionFile).Replace("label: confirmed_official", "sequence: 1\nlabel: confirmed_official", StringComparison.Ordinal));
        var (fixedCode, fixedOutput, _) = await DclRunner.RunAsync("validate", "--data", current, "--previous-data", previous);

        fixedCode.ShouldBe(ExitCodes.Ok, fixedOutput);
    }

    [Fact]
    public async Task Missing_previous_data_directory_is_a_usage_error()
    {
        var (code, _, error) = await DclRunner.RunAsync("validate", "--data", FixturePath("valid"), "--previous-data", Path.Combine(_temp, "nowhere"));

        code.ShouldBe(ExitCodes.Usage);
        error.ShouldContain("Previous data directory not found");
    }

    [Fact]
    public async Task Missing_data_directory_is_a_usage_error()
    {
        var (code, _, error) = await DclRunner.RunAsync("validate", "--data", Path.Combine(_temp, "nowhere"));

        code.ShouldBe(ExitCodes.Usage);
        error.ShouldContain("Data directory not found");
    }

    [Fact]
    public async Task Unknown_format_is_a_usage_error()
    {
        var (code, _, _) = await DclRunner.RunAsync("validate", "--data", FixturePath("valid"), "--format", "xml");

        code.ShouldBe(ExitCodes.Usage);
    }

    public void Dispose()
    {
        Directory.Delete(_temp, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>A copy of the valid fixture ledger under the test's temporary directory.</summary>
    private string CopyFixture(string name)
    {
        var source = FixturePath("valid");
        var target = Path.Combine(_temp, name);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        return target;
    }

    private string WriteEvent(string yaml)
    {
        var data = Path.Combine(_temp, "data");
        Directory.CreateDirectory(Path.Combine(data, "events"));
        File.WriteAllText(Path.Combine(data, "events", "fixture-boc.yaml"), yaml);
        return data;
    }

    private static string FixturePath(string name) => DclRunner.RepositoryPath("tests", "fixtures", "ledger", name);
}
