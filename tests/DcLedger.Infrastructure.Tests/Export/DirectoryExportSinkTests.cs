using DcLedger.Application;
using DcLedger.Infrastructure.Export;
using Shouldly;
using Xunit;

namespace DcLedger.Infrastructure.Tests.Export;

public sealed class DirectoryExportSinkTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("dcl-sink-").FullName;

    [Fact]
    public void Files_are_written_byte_for_byte_into_nested_folders_that_are_created()
    {
        var output = Path.Combine(_temp, "out");
        byte[] content = [0x42, 0x0D, 0x0A, 0xC3, 0xA9];

        new DirectoryExportSink(output).Write(new ExportFile("public/radar/fixture-county.ics", content));

        File.ReadAllBytes(Path.Combine(output, "public", "radar", "fixture-county.ics")).ShouldBe(content);
    }

    [Fact]
    public void An_existing_file_is_replaced_and_no_temporary_file_is_left()
    {
        var sink = new DirectoryExportSink(_temp);
        sink.Write(new ExportFile("events.csv", "old, longer content"u8.ToArray()));

        sink.Write(new ExportFile("events.csv", "new"u8.ToArray()));

        File.ReadAllText(Path.Combine(_temp, "events.csv")).ShouldBe("new");
        Directory.GetFiles(_temp).Select(Path.GetFileName).ShouldHaveSingleItem().ShouldBe("events.csv");
    }

    [Fact]
    public void Other_files_in_the_output_directory_are_left_alone()
    {
        var keep = Path.Combine(_temp, "keep.txt");
        File.WriteAllText(keep, "mine");

        new DirectoryExportSink(_temp).Write(new ExportFile("site-data/events.json", "{}"u8.ToArray()));

        File.ReadAllText(keep).ShouldBe("mine");
    }

    public void Dispose()
    {
        Directory.Delete(_temp, recursive: true);
        GC.SuppressFinalize(this);
    }
}
