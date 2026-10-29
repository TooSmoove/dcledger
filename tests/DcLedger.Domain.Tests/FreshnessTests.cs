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

    [Fact]
    public void Offsets_do_not_matter_only_instants()
    {
        var eastern = new DateTimeOffset(2026, 11, 3, 7, 0, 0, TimeSpan.FromHours(-5)); // 12:00Z, exactly 48 h before AsOf

        Freshness.IsStale(eastern, AsOf).ShouldBeFalse();
    }
}
