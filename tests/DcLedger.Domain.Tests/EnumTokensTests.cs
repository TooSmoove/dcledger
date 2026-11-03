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
    public void Action_type_tokens_are_fixed()
    {
        ActionType.Moratorium.ToToken().ShouldBe("moratorium");
        ActionType.Rezoning.ToToken().ShouldBe("rezoning");
        ActionType.TextAmendment.ToToken().ShouldBe("text_amendment");
        ActionType.Resolution.ToToken().ShouldBe("resolution");
    }

    [Fact]
    public void Action_outcome_tokens_are_fixed()
    {
        ActionOutcome.Adopted.ToToken().ShouldBe("adopted");
        ActionOutcome.AdoptedAmended.ToToken().ShouldBe("adopted_amended");
        ActionOutcome.Failed.ToToken().ShouldBe("failed");
        ActionOutcome.Continued.ToToken().ShouldBe("continued");
        ActionOutcome.Tabled.ToToken().ShouldBe("tabled");
    }

    [Fact]
    public void Every_member_has_a_distinct_token()
    {
        Enum.GetValues<EventStatus>().Select(value => value.ToToken()).ShouldBeUnique();
        Enum.GetValues<EvidenceLabel>().Select(value => value.ToToken()).ShouldBeUnique();
        Enum.GetValues<SourceType>().Select(value => value.ToToken()).ShouldBeUnique();
        Enum.GetValues<ActionType>().Select(value => value.ToToken()).ShouldBeUnique();
        Enum.GetValues<ActionOutcome>().Select(value => value.ToToken()).ShouldBeUnique();
    }

    [Fact]
    public void An_undefined_value_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ((EventStatus)99).ToToken());
        Should.Throw<ArgumentOutOfRangeException>(() => ((EvidenceLabel)99).ToToken());
        Should.Throw<ArgumentOutOfRangeException>(() => ((SourceType)99).ToToken());
        Should.Throw<ArgumentOutOfRangeException>(() => ((ActionType)99).ToToken());
        Should.Throw<ArgumentOutOfRangeException>(() => ((ActionOutcome)99).ToToken());
    }
}
