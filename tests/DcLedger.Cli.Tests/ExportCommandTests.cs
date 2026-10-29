using Shouldly;
using Xunit;

namespace DcLedger.Cli.Tests;

public sealed class ExportCommandTests : IDisposable
{
    private static readonly string[] ExpectedFiles =
    [
        "public/events.csv",
        "public/radar.ics",
        "public/radar/fixture-city.ics",
        "public/radar/fixture-county.ics",
        "site-data/events.json",
    ];

    private readonly string _temp = Directory.CreateTempSubdirectory("dcl-export-").FullName;

    [Fact]
    public async Task The_fixture_ledger_exports_every_file()
    {
        var output = Path.Combine(_temp, "out");

        var (code, stdout, stderr) = await ExportAsync(Fixture(), Status(), output);

        code.ShouldBe(ExitCodes.Ok, stderr);
        stdout.ShouldContain("Exported 3 event(s) for 2 jurisdiction(s)");
        ListFiles(output).ShouldBe(ExpectedFiles);
        File.ReadAllText(Path.Combine(output, "site-data", "events.json")).ShouldContain("\"starts_utc\": \"2026-11-05T22:30:00Z\"");
    }

    [Fact]
    public async Task Exporting_twice_gives_byte_identical_files()
    {
        var first = Path.Combine(_temp, "first");
        var second = Path.Combine(_temp, "second");

        (await ExportAsync(Fixture(), Status(), first)).Code.ShouldBe(ExitCodes.Ok);
        (await ExportAsync(Fixture(), Status(), second)).Code.ShouldBe(ExitCodes.Ok);

        foreach (var file in ExpectedFiles)
        {
            File.ReadAllBytes(Path.Combine(second, file)).SequenceEqual(File.ReadAllBytes(Path.Combine(first, file))).ShouldBeTrue(file);
        }
    }

    [Fact]
    public async Task An_invalid_ledger_exits_1_and_writes_nothing()
    {
        var data = Path.Combine(_temp, "data");
        Directory.CreateDirectory(Path.Combine(data, "events"));
        File.WriteAllText(Path.Combine(data, "events", "fixture-boc.yaml"), "id: fixture-boc\n");
        var output = Path.Combine(_temp, "out");

        var (code, stdout, _) = await ExportAsync(data, Status(), output);

        code.ShouldBe(ExitCodes.DataInvalid);
        stdout.ShouldContain("[REQUIRED_FIELD]");
        stdout.ShouldContain("nothing was exported");
        Directory.Exists(output).ShouldBeFalse();
    }

    [Fact]
    public async Task A_missing_status_file_is_a_usage_error()
    {
        var (code, _, stderr) = await ExportAsync(Fixture(), Path.Combine(_temp, "nowhere.json"), Path.Combine(_temp, "out"));

        code.ShouldBe(ExitCodes.Usage);
        stderr.ShouldContain("nowhere.json");
    }

    [Fact]
    public async Task A_malformed_status_file_is_a_usage_error()
    {
        var status = Path.Combine(_temp, "status.json");
        File.WriteAllText(status, """{ "schema_version": 1, "generated_utc": "yesterday", "jurisdictions": [] }""");

        var (code, _, stderr) = await ExportAsync(Fixture(), status, Path.Combine(_temp, "out"));

        code.ShouldBe(ExitCodes.Usage);
        stderr.ShouldContain("generated_utc");
    }

    [Fact]
    public async Task A_missing_data_directory_is_a_usage_error()
    {
        var (code, _, stderr) = await ExportAsync(Path.Combine(_temp, "nowhere"), Status(), Path.Combine(_temp, "out"));

        code.ShouldBe(ExitCodes.Usage);
        stderr.ShouldContain("Data directory not found");
    }

    [Fact]
    public async Task Missing_options_are_usage_errors()
    {
        var (code, _, _) = await DclRunner.RunAsync("export", "--data", Fixture());

        code.ShouldBe(ExitCodes.Usage);
    }

    [Fact]
    public async Task Status_entries_for_unknown_jurisdictions_warn_without_failing()
    {
        var status = Path.Combine(_temp, "status.json");
        File.WriteAllText(status, """
            { "schema_version": 1, "generated_utc": "2026-11-05T14:47:00Z",
              "jurisdictions": [ { "id": "retired-county", "last_checked_utc": null } ] }
            """);

        var (code, stdout, _) = await ExportAsync(Fixture(), status, Path.Combine(_temp, "out"), "--format", "github");

        code.ShouldBe(ExitCodes.Ok);
        stdout.ShouldContain("::warning title=dcl export::The ingest status lists 'retired-county'");
    }

    public void Dispose()
    {
        Directory.Delete(_temp, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static Task<(int Code, string Output, string Error)> ExportAsync(string data, string status, string output, params string[] extra) =>
        DclRunner.RunAsync(["export", "--data", data, "--status", status, "--out", output, .. extra]);

    private static List<string> ListFiles(string root) =>
    [
        .. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal),
    ];

    private static string Fixture() => DclRunner.RepositoryPath("tests", "fixtures", "ledger", "export");

    private static string Status() => DclRunner.RepositoryPath("tests", "fixtures", "ingest-status", "valid.json");
}
