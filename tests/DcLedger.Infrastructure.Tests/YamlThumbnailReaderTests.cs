using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Infrastructure.Ledger;
using Shouldly;
using Xunit;

namespace DcLedger.Infrastructure.Tests;

/// <summary>Reading a document's <c>kind</c> and <c>thumbnail</c>, and the picture files in <c>data/thumbnails/</c> (issue #132).</summary>
public sealed class YamlThumbnailReaderTests
{
    private const string DocumentId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string PictureHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    /// <summary>The smallest bytes the reader accepts as WebP: a RIFF header with form type WEBP.</summary>
    private static readonly byte[] WebP = [.. "RIFF"u8, 4, 0, 0, 0, .. "WEBP"u8];

    private static string Record(string extra) => $"""
        id: {DocumentId}
        url: https://fixture.example.org/minutes.pdf
        retrieved_at: 2026-11-10T12:00:00Z
        media_type: application/pdf
        bytes: 1234
        source_type: official
        {extra}
        """;

    private const string ThumbnailBlock = $"""
        kind: minutes
        thumbnail:
          sha256: {PictureHash}
          width: 240
          height: 311
          reviewed: true
        """;

    private static LedgerSnapshot Load(Action<TempLedger> write)
    {
        using var ledger = new TempLedger();
        write(ledger);
        return new YamlLedgerReader().Load(ledger.Path);
    }

    [Fact]
    public void A_document_without_kind_or_thumbnail_reads_as_before()
    {
        var snapshot = Load(l => l.Write($"documents/{DocumentId}.yaml", Record("")));

        snapshot.LoadErrors.ShouldBeEmpty();
        var document = snapshot.Documents.ShouldHaveSingleItem().Record;
        document.Kind.ShouldBeNull();
        document.Title.ShouldBeNull();
        document.Thumbnail.ShouldBeNull();
        snapshot.Thumbnails.ShouldBeEmpty();
    }

    [Fact]
    public void A_title_is_read_as_written()
    {
        var snapshot = Load(l => l.Write($"documents/{DocumentId}.yaml", Record("title: \"Aug 17, 2026 special meeting minutes\"")));

        snapshot.LoadErrors.ShouldBeEmpty();
        snapshot.Documents.ShouldHaveSingleItem().Record.Title.ShouldBe("Aug 17, 2026 special meeting minutes");
    }

    [Fact]
    public void Kind_and_thumbnail_are_read()
    {
        var snapshot = Load(l => l.Write($"documents/{DocumentId}.yaml", Record(ThumbnailBlock)));

        snapshot.LoadErrors.ShouldBeEmpty();
        var document = snapshot.Documents.ShouldHaveSingleItem().Record;
        document.Kind.ShouldBe(DocumentKind.Minutes);
        var thumbnail = document.Thumbnail.ShouldNotBeNull();
        thumbnail.Sha256.Value.ShouldBe(PictureHash);
        thumbnail.Width.ShouldBe(240);
        thumbnail.Height.ShouldBe(311);
        thumbnail.Reviewed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("kind: packet")]
    [InlineData("title: \"\"")]
    [InlineData("title: \"" + "Aug 17, 2026 special meeting minutes, recessed and reconvened Aug 18, as approved" + "\"")]
    [InlineData("title: |\n      Aug 17, 2026\n      special meeting minutes")]
    [InlineData("thumbnail: yes")]
    [InlineData("thumbnail:\n      sha256: " + PictureHash + "\n      width: 0\n      height: 311\n      reviewed: true")]
    [InlineData("thumbnail:\n      sha256: " + PictureHash + "\n      width: 240\n      height: 9999\n      reviewed: true")]
    [InlineData("thumbnail:\n      sha256: " + PictureHash + "\n      width: 240\n      height: 311\n      reviewed: yes")]
    [InlineData("thumbnail:\n      sha256: " + PictureHash + "\n      width: 240\n      height: 311")]
    [InlineData("thumbnail:\n      sha256: " + PictureHash + "\n      width: 240\n      height: 311\n      reviewed: true\n      reviewer: someone")]
    public void A_malformed_kind_title_or_thumbnail_is_reported(string extra)
    {
        var snapshot = Load(l => l.Write($"documents/{DocumentId}.yaml", Record(extra)));

        snapshot.LoadErrors.ShouldNotBeEmpty();
        snapshot.Documents.ShouldBeEmpty();
    }

    [Fact]
    public void A_picture_file_is_read_with_its_document_id_and_hash()
    {
        var snapshot = Load(l => l.WriteBytes($"thumbnails/{DocumentId}.webp", WebP));

        snapshot.LoadErrors.ShouldBeEmpty();
        var file = snapshot.Thumbnails.ShouldHaveSingleItem();
        file.File.ShouldBe($"thumbnails/{DocumentId}.webp");
        file.DocumentId.Value.ShouldBe(DocumentId);
        file.ContentHash.ShouldBe(Sha256Hash.Compute(WebP));
        file.Content.ToArray().ShouldBe(WebP);
        snapshot.RecordFiles.ShouldBeEmpty("pictures aren't records, so removing one is not a renamed ID");
    }

    [Theory]
    [InlineData("thumbnails/not-a-hash.webp")]
    [InlineData("thumbnails/" + DocumentId + ".png")]
    public void A_picture_with_another_name_is_reported(string path)
    {
        var snapshot = Load(l => l.WriteBytes(path, WebP));

        snapshot.LoadErrors.ShouldHaveSingleItem().Rule.ShouldBe(ValidationRule.ThumbnailInvalid);
        snapshot.Thumbnails.ShouldBeEmpty();
    }

    [Fact]
    public void A_file_that_is_not_webp_is_reported()
    {
        var snapshot = Load(l => l.WriteBytes($"thumbnails/{DocumentId}.webp", [.. "%PDF-1.7"u8]));

        snapshot.LoadErrors.ShouldHaveSingleItem().Rule.ShouldBe(ValidationRule.ThumbnailInvalid);
    }

    [Fact]
    public void A_picture_over_the_size_cap_is_reported()
    {
        var large = new byte[Thumbnail.MaxBytes + 1];
        WebP.CopyTo(large, 0);

        var snapshot = Load(l => l.WriteBytes($"thumbnails/{DocumentId}.webp", large));

        snapshot.LoadErrors.ShouldHaveSingleItem().Rule.ShouldBe(ValidationRule.FileTooLarge);
    }
}
