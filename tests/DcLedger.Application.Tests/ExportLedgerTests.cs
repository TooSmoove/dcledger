using DcLedger.Testing;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

public sealed class ExportLedgerTests
{
    private static readonly string[] PathOrder = ["a/first.txt", "b/second.txt", "c.txt"];

    private readonly RecordingSink _sink = new();

    [Fact]
    public void A_valid_ledger_is_exported_and_files_are_written_in_path_order()
    {
        var exporters = new IExporter[] { new FakeExporter("b/second.txt", "a/first.txt"), new FakeExporter("c.txt") };

        var report = new ExportLedger(new FakeReader(ExportSamples.Snapshot()), exporters, _sink).Run("data", ExportSamples.Status());

        report.Exported.ShouldBeTrue();
        report.Files.ShouldBe(PathOrder);
        _sink.Written.ShouldBe(PathOrder);
        report.Events.ShouldBe(3);
        report.Jurisdictions.ShouldBe(2);
        report.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void An_invalid_ledger_writes_nothing_and_reports_every_problem()
    {
        var invalid = ExportSamples.Snapshot() with { Documents = [] };
        var exporter = new FakeExporter("never.txt");

        var report = new ExportLedger(new FakeReader(invalid), [exporter], _sink).Run("data", ExportSamples.Status());

        report.Exported.ShouldBeFalse();
        report.Validation.Errors.Count(error => error.Rule == ValidationRule.DocMissing).ShouldBe(4);
        exporter.Calls.ShouldBe(0);
        _sink.Written.ShouldBeEmpty();
        report.Files.ShouldBeEmpty();
    }

    [Fact]
    public void Warnings_from_the_model_are_reported()
    {
        var status = ExportSamples.Status() with
        {
            LastCheckedUtc = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal) { ["retired-county"] = null },
        };

        var report = new ExportLedger(new FakeReader(ExportSamples.Snapshot()), [], _sink).Run("data", status);

        report.Warnings.ShouldHaveSingleItem().ShouldContain("'retired-county'");
    }

    [Fact]
    public void Two_exporters_writing_the_same_path_is_a_bug_and_nothing_is_written()
    {
        var exporters = new IExporter[] { new FakeExporter("same.txt"), new FakeExporter("same.txt") };

        Should.Throw<InvalidOperationException>(() =>
            new ExportLedger(new FakeReader(ExportSamples.Snapshot()), exporters, _sink).Run("data", ExportSamples.Status()));
        _sink.Written.ShouldBeEmpty();
    }

    [Fact]
    public void A_failing_exporter_leaves_the_output_untouched()
    {
        var exporters = new IExporter[] { new FakeExporter("first.txt"), new ThrowingExporter() };

        Should.Throw<InvalidOperationException>(() =>
            new ExportLedger(new FakeReader(ExportSamples.Snapshot()), exporters, _sink).Run("data", ExportSamples.Status()));
        _sink.Written.ShouldBeEmpty();
    }

    private sealed class FakeReader(LedgerSnapshot snapshot) : ILedgerReader
    {
        public LedgerSnapshot Load(string dataDirectory) => snapshot;
    }

    private sealed class FakeExporter(params string[] paths) : IExporter
    {
        public int Calls { get; private set; }

        public IReadOnlyList<ExportFile> Export(ExportModel model)
        {
            Calls++;
            return [.. paths.Select(path => new ExportFile(path, "x"u8.ToArray()))];
        }
    }

    private sealed class ThrowingExporter : IExporter
    {
        public IReadOnlyList<ExportFile> Export(ExportModel model) => throw new InvalidOperationException("exporter failed");
    }

    private sealed class RecordingSink : IExportSink
    {
        public List<string> Written { get; } = [];

        public void Write(ExportFile file) => Written.Add(file.Path);
    }
}
