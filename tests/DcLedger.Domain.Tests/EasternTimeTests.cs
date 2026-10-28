using Shouldly;
using Xunit;

namespace DcLedger.Domain.Tests;

public sealed class EasternTimeTests
{
    [Theory]
    [InlineData(2026, 10, 15, 17, 30, 2026, 10, 15, 21, 30)] // EDT, UTC-4 (SPEC AC3)
    [InlineData(2026, 11, 5, 17, 30, 2026, 11, 5, 22, 30)] // EST, UTC-5, after DST ends Nov 1 (SPEC AC3)
    public void Wall_time_converts_to_the_expected_utc_instant(
        int year, int month, int day, int hour, int minute,
        int utcYear, int utcMonth, int utcDay, int utcHour, int utcMinute)
    {
        EasternTime.TryToInstant(new DateTime(year, month, day, hour, minute, 0), out var instant, out var issue).ShouldBeTrue();

        issue.ShouldBe(WallTimeIssue.None);
        instant.UtcDateTime.ShouldBe(new DateTime(utcYear, utcMonth, utcDay, utcHour, utcMinute, 0, DateTimeKind.Utc));
        EasternTime.HasEasternOffset(instant).ShouldBeTrue();
    }

    [Fact]
    public void Time_skipped_when_daylight_saving_starts_is_rejected()
    {
        EasternTime.TryToInstant(new DateTime(2027, 3, 14, 2, 30, 0), out _, out var issue).ShouldBeFalse();

        issue.ShouldBe(WallTimeIssue.SkippedByDaylightSaving);
    }

    [Fact]
    public void Time_repeated_when_daylight_saving_ends_is_rejected()
    {
        EasternTime.TryToInstant(new DateTime(2026, 11, 1, 1, 30, 0), out _, out var issue).ShouldBeFalse();

        issue.ShouldBe(WallTimeIssue.RepeatedByDaylightSaving);
    }

    [Fact]
    public void Kind_of_the_wall_time_is_ignored()
    {
        var utcKind = new DateTime(2026, 11, 5, 17, 30, 0, DateTimeKind.Utc);

        EasternTime.TryToInstant(utcKind, out var instant, out _).ShouldBeTrue();

        instant.ShouldBe(new DateTimeOffset(2026, 11, 5, 17, 30, 0, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void Instant_is_expressed_in_eastern_time_with_the_offset_then_in_effect()
    {
        var eastern = EasternTime.ToEastern(new DateTimeOffset(2026, 11, 5, 22, 30, 0, TimeSpan.Zero));

        eastern.ShouldBe(new DateTimeOffset(2026, 11, 5, 17, 30, 0, TimeSpan.FromHours(-5)));
        eastern.Offset.ShouldBe(TimeSpan.FromHours(-5));
    }

    [Fact]
    public void Daylight_offset_after_daylight_saving_ends_is_not_eastern()
    {
        var wrongOffset = new DateTimeOffset(2026, 11, 5, 18, 30, 0, TimeSpan.FromHours(-4));

        EasternTime.HasEasternOffset(wrongOffset).ShouldBeFalse();
    }
}
