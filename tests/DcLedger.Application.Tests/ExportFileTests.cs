using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

public sealed class ExportFileTests
{
    [Theory]
    [InlineData("site-data/events.json")]
    [InlineData("public/radar/fixture-county.ics")]
    [InlineData("events.csv")]
    public void Safe_relative_paths_are_accepted(string path)
    {
        new ExportFile(path, ReadOnlyMemory<byte>.Empty).Path.ShouldBe(path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/etc/passwd")]
    [InlineData("../outside.txt")]
    [InlineData("public/../../outside.txt")]
    [InlineData("./events.json")]
    [InlineData("public//radar.ics")]
    [InlineData("public/")]
    [InlineData(@"public\radar.ics")]
    [InlineData("C:/radar.ics")]
    [InlineData("Public/Radar.ics")]
    [InlineData(".hidden")]
    [InlineData("public/radar ics")]
    public void Unsafe_paths_are_rejected(string path)
    {
        Should.Throw<ArgumentException>(() => new ExportFile(path, ReadOnlyMemory<byte>.Empty));
    }
}
