using Shouldly;
using Xunit;

namespace DcLedger.Domain.Tests;

public sealed class FreshnessTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(47, false)]
    [InlineData(48, false)]
    [InlineData(49, true)]
    public void A_check_goes_stale_after_48_hours(int hoursAgo, bool stale)
    {
        Freshness.IsStale(AsOf.AddHours(-hoursAgo), AsOf).ShouldBe(stale);
    }

    [Fact]
    public void One_second_past_48_hours_is_stale()
    {
        Freshness.IsStale(AsOf - Freshness.StaleAfter - TimeSpan.FromSeconds(1), AsOf).ShouldBeTrue();
    }

    [Fact]
    public void Never_checked_is_stale()
    {
        Freshness.IsStale(null, AsOf).ShouldBeTrue();
    }

    [Fact]
    public void A_check_later_than_as_of_is_fresh()
    {
        Freshness.IsStale(AsOf.AddMinutes(5), AsOf).ShouldBeFalse();
    }

    [Theory]
    [InlineData(Platform.Legistar, 48)]
    [InlineData(Platform.Manual, 168)]
    public void A_manual_check_stays_fresh_for_7_days_an_automated_one_for_48_hours(Platform platform, int hours)
    {
        var staleAfter = Freshness.StaleAfterFor(platform);

        staleAfter.ShouldBe(TimeSpan.FromHours(hours));
        Freshness.IsStale(AsOf - staleAfter, AsOf, staleAfter).ShouldBeFalse("exactly the threshold is still fresh");
        Freshness.IsStale(AsOf - staleAfter - TimeSpan.FromSeconds(1), AsOf, staleAfter).ShouldBeTrue();
        Freshness.IsStale(null, AsOf, staleAfter).ShouldBeTrue();
    }

    [Fact]
    public void Offsets_do_not_matter_only_instants()
    {
        var eastern = new DateTimeOffset(2026, 11, 3, 7, 0, 0, TimeSpan.FromHours(-5)); // 12:00Z, exactly 48 h before AsOf

        Freshness.IsStale(eastern, AsOf).ShouldBeFalse();
    }
}
