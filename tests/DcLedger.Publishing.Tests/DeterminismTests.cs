using System.Globalization;
using DcLedger.Application;
using DcLedger.Publishing.Ics;
using DcLedger.Publishing.SiteData;
using DcLedger.Publishing.Tabular;
using DcLedger.Testing;
using Shouldly;
using Xunit;

namespace DcLedger.Publishing.Tests;

/// <summary>
/// The same model gives the same bytes on every run, whatever the machine's culture (WS-AC2). ar-SA formats dates in
/// the Umm al-Qura calendar and tr-TR has the dotted-I casing rules, so any culture-sensitive formatting shows up here.
/// </summary>
public sealed class DeterminismTests
{
    public static TheoryData<string> Cultures() => new() { "tr-TR", "de-DE", "ar-SA", "ja-JP" };

    [Fact]
    public void Exporting_twice_gives_identical_bytes()
    {
        Snapshot(Export()).ShouldBe(Snapshot(Export()));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_machine_culture_does_not_change_the_bytes(string culture)
    {
        var invariant = WithCulture(CultureInfo.InvariantCulture, Export);

        var local = WithCulture(CultureInfo.GetCultureInfo(culture), Export);

        Snapshot(local).ShouldBe(Snapshot(invariant));
    }

    private static List<ExportFile> Export()
    {
        var model = ExportSamples.Model();
        IExporter[] exporters = [new SiteDataExporter(), new RadarCalendarExporter(), new TabularExporter()];
        return [.. exporters.SelectMany(exporter => exporter.Export(model))];
    }

    private static List<string> Snapshot(List<ExportFile> files) =>
        [.. files.Select(file => $"{file.Path} {Convert.ToHexStringLower(file.Content.Span)}")];

    private static T WithCulture<T>(CultureInfo culture, Func<T> action)
    {
        var (current, currentUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
            CultureInfo.CurrentUICulture = currentUi;
        }
    }
}
