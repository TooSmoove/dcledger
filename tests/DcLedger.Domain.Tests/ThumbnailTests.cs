using Shouldly;
using Xunit;

namespace DcLedger.Domain.Tests;

/// <summary>Which documents may show their first page (issue #132, SPEC §E).</summary>
public sealed class ThumbnailTests
{
    [Theory]
    [InlineData(DocumentKind.Minutes)]
    [InlineData(DocumentKind.Legislation)]
    public void Official_minutes_and_legislation_may_be_shown(DocumentKind kind)
    {
        Thumbnail.MayShow(kind, SourceType.Official).ShouldBeTrue();
    }

    [Fact]
    public void No_other_official_kind_may_be_shown()
    {
        Enum.GetValues<DocumentKind>()
            .Where(kind => kind is not (DocumentKind.Minutes or DocumentKind.Legislation))
            .ShouldAllBe(kind => !Thumbnail.MayShow(kind, SourceType.Official));
    }

    [Fact]
    public void Nothing_from_the_news_and_nothing_of_unknown_kind_may_be_shown()
    {
        Enum.GetValues<DocumentKind>().ShouldAllBe(kind => !Thumbnail.MayShow(kind, SourceType.News));
        Thumbnail.MayShow(null, SourceType.Official).ShouldBeFalse();
    }
}
