using Shouldly;
using Xunit;

namespace DcLedger.Domain.Tests;

/// <summary>The tokens are a public data contract (YAML and every export); these pin them.</summary>
public sealed class EnumTokensTests
{
    [Fact]
    public void Event_status_tokens_are_fixed()
    {
        EventStatus.Scheduled.ToToken().ShouldBe("scheduled");
        EventStatus.Cancelled.ToToken().ShouldBe("cancelled");
    }

    [Fact]
    public void Evidence_label_tokens_are_fixed()
    {
        EvidenceLabel.ReportedNews.ToToken().ShouldBe("reported_news");
        EvidenceLabel.ConfirmedOfficial.ToToken().ShouldBe("confirmed_official");
    }

    [Fact]
    public void Source_type_tokens_are_fixed()
    {
        SourceType.Official.ToToken().ShouldBe("official");
        SourceType.News.ToToken().ShouldBe("news");
    }

    [Fact]
    public void Every_member_has_a_distinct_token()
    {
        Enum.GetValues<EventStatus>().Select(value => value.ToToken()).ShouldBeUnique();
        Enum.GetValues<EvidenceLabel>().Select(value => value.ToToken()).ShouldBeUnique();
        Enum.GetValues<SourceType>().Select(value => value.ToToken()).ShouldBeUnique();
    }

    [Fact]
    public void An_undefined_value_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ((EventStatus)99).ToToken());
        Should.Throw<ArgumentOutOfRangeException>(() => ((EvidenceLabel)99).ToToken());
        Should.Throw<ArgumentOutOfRangeException>(() => ((SourceType)99).ToToken());
    }
}
