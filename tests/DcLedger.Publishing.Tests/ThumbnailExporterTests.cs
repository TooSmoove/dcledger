using System.Text.Json;
using DcLedger.Publishing.SiteData;
using DcLedger.Publishing.Thumbnails;
using DcLedger.Testing;
using Shouldly;
using Xunit;

namespace DcLedger.Publishing.Tests;

/// <summary>First-page pictures in the export (issue #132): copied byte for byte, and described in events.json.</summary>
public sealed class ThumbnailExporterTests
{
    private static readonly string DetailId = ExportSamples.CountyActionDetail1600.Id.Value;

    [Fact]
    public void Each_listed_document_s_picture_is_copied_byte_for_byte()
    {
        var file = new ThumbnailExporter().Export(ExportSamples.ModelWithThumbnail()).ShouldHaveSingleItem();

        file.Path.ShouldBe($"public/thumbnails/{DetailId}.webp");
        file.Content.ToArray().ShouldBe(ExportSamples.ThumbnailBytes);
    }

    [Fact]
    public void A_ledger_without_pictures_exports_none()
    {
        new ThumbnailExporter().Export(ExportSamples.Model()).ShouldBeEmpty();
    }

    [Fact]
    public void Events_json_gives_the_document_s_kind_title_and_its_picture_s_path_and_size()
    {
        var json = TestFiles.Text(TestFiles.Single(new SiteDataExporter().Export(ExportSamples.ModelWithThumbnail()), ExportPaths.SiteData));
        using var document = JsonDocument.Parse(json);

        var source = document.RootElement.GetProperty("actions")[0].GetProperty("documents")[0];
        source.GetProperty("sha256").GetString().ShouldBe(DetailId);
        source.GetProperty("kind").GetString().ShouldBe("legislation");
        source.GetProperty("title").GetString().ShouldBe("Item 2026-1600");
        var thumbnail = source.GetProperty("thumbnail");
        thumbnail.GetProperty("path").GetString().ShouldBe($"/thumbnails/{DetailId}.webp");
        thumbnail.GetProperty("width").GetInt32().ShouldBe(240);
        thumbnail.GetProperty("height").GetInt32().ShouldBe(311);
        thumbnail.EnumerateObject().Select(p => p.Name).ShouldBe(["path", "width", "height"]);
    }
}
