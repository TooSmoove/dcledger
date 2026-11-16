using DcLedger.Domain;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

/// <summary>The gate's rules for first-page pictures (issue #132): one passing and one failing case per rule.</summary>
public sealed class ThumbnailRulesTests
{
    private static readonly byte[] Picture = [.. "RIFF"u8, 4, 0, 0, 0, .. "WEBP"u8];
    private static readonly Sha256Hash PictureHash = Sha256Hash.Compute(Picture);

    private static readonly Document Minutes = new()
    {
        Id = Sha256Hash.Compute("fixture minutes"u8),
        Url = new Uri("https://fixture.example.org/minutes.pdf"),
        RetrievedAtUtc = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero),
        MediaType = "application/pdf",
        Bytes = 1234,
        SourceType = SourceType.Official,
        Kind = DocumentKind.Minutes,
        Thumbnail = new Thumbnail { Sha256 = PictureHash, Width = 240, Height = 311, Reviewed = true },
    };

    private static ThumbnailFile FileFor(Document document, byte[] content) =>
        new($"thumbnails/{document.Id}.webp", document.Id, Sha256Hash.Compute(content), content);

    private static List<ValidationError> Validate(Document document, params ThumbnailFile[] files)
    {
        var errors = new List<ValidationError>();
        ThumbnailRules.Check(errors, [new LoadedRecord<Document>($"documents/{document.Id}.yaml", document)], files);
        return errors;
    }

    [Fact]
    public void Reviewed_official_minutes_with_a_matching_file_are_valid()
    {
        Validate(Minutes, FileFor(Minutes, Picture)).ShouldBeEmpty();
    }

    [Fact]
    public void A_reviewed_official_legislation_page_is_valid()
    {
        Validate(Minutes with { Kind = DocumentKind.Legislation }, FileFor(Minutes, Picture)).ShouldBeEmpty();
    }

    [Fact]
    public void A_document_without_a_thumbnail_and_no_file_is_valid()
    {
        Validate(Minutes with { Thumbnail = null }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DocumentKind.AgendaPacket, SourceType.Official)]
    [InlineData(DocumentKind.Agenda, SourceType.Official)]
    [InlineData(DocumentKind.NewsRelease, SourceType.Official)]
    [InlineData(DocumentKind.Minutes, SourceType.News)]
    [InlineData(DocumentKind.NewsArticle, SourceType.News)]
    public void Only_official_minutes_and_legislation_may_show_a_picture(DocumentKind kind, SourceType sourceType)
    {
        var error = Validate(Minutes with { Kind = kind, SourceType = sourceType }, FileFor(Minutes, Picture)).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.ThumbnailInvalid);
        error.Message.ShouldContain("agenda packets stay private");
    }

    [Fact]
    public void A_document_of_unknown_kind_may_not_show_a_picture()
    {
        Validate(Minutes with { Kind = null }, FileFor(Minutes, Picture)).ShouldHaveSingleItem().Rule.ShouldBe(ValidationRule.ThumbnailInvalid);
    }

    [Fact]
    public void A_picture_nobody_reviewed_is_reported()
    {
        var unreviewed = Minutes with { Thumbnail = Minutes.Thumbnail! with { Reviewed = false } };

        var error = Validate(unreviewed, FileFor(Minutes, Picture)).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.ThumbnailUnreviewed);
        error.RecordId.ShouldBe(Minutes.Id.Value);
    }

    [Fact]
    public void A_missing_file_is_reported()
    {
        var error = Validate(Minutes).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.ThumbnailInvalid);
        error.Message.ShouldContain("is missing");
    }

    [Fact]
    public void A_file_that_differs_from_the_reviewed_hash_is_reported()
    {
        var error = Validate(Minutes, FileFor(Minutes, [.. Picture, 0])).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.ThumbnailInvalid);
        error.Message.ShouldContain(PictureHash.Value);
    }

    [Fact]
    public void A_file_no_record_claims_is_reported()
    {
        var error = Validate(Minutes with { Thumbnail = null }, FileFor(Minutes, Picture)).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.ThumbnailInvalid);
        error.File.ShouldBe($"thumbnails/{Minutes.Id}.webp");
    }

    [Fact]
    public void The_validator_runs_these_rules()
    {
        var snapshot = new LedgerSnapshot
        {
            Documents = [new LoadedRecord<Document>($"documents/{Minutes.Id}.yaml", Minutes with { Thumbnail = Minutes.Thumbnail! with { Reviewed = false } })],
            Thumbnails = [FileFor(Minutes, Picture)],
        };

        LedgerValidator.Validate(snapshot, []).ShouldContain(error => error.Rule == ValidationRule.ThumbnailUnreviewed);
    }
}
