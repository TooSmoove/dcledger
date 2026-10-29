using Shouldly;
using Xunit;

namespace DcLedger.Cli.Tests;

public sealed class CronSlotTests
{
    [Theory]
    [InlineData("17 */6 * * *", "2026-10-29T06:29:30Z", "2026-10-29T06:17:00Z")]
    [InlineData("17 */6 * * *", "2026-10-29T06:17:00Z", "2026-10-29T06:17:00Z")]
    [InlineData("17 */6 * * *", "2026-10-29T06:16:59Z", "2026-10-29T00:17:00Z")]
    [InlineData("17 */6 * * *", "2026-10-29T00:05:00Z", "2026-10-28T18:17:00Z")]
    [InlineData("17 */4 * * *", "2026-10-29T09:00:00Z", "2026-10-29T08:17:00Z")]
    [InlineData("17 * * * *", "2026-10-29T09:00:00Z", "2026-10-29T08:17:00Z")]
    [InlineData("5,35 1-3 * * *", "2026-10-29T02:50:00Z", "2026-10-29T02:35:00Z")]
    [InlineData("0/20 12 * * *", "2026-10-29T12:59:00Z", "2026-10-29T12:40:00Z")]
    [InlineData("  17   */6 * * * ", "2026-10-29T06:29:30Z", "2026-10-29T06:17:00Z")]
    public void Finds_the_latest_slot_at_or_before_now(string schedule, string now, string expected)
    {
        CronSlot.Previous(schedule, DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Works_in_utc_whatever_the_offset_of_now()
    {
        var easternMorning = new DateTimeOffset(2026, 10, 29, 2, 29, 0, TimeSpan.FromHours(-4)); // 06:29 UTC

        CronSlot.Previous("17 */6 * * *", easternMorning).ShouldBe(new DateTimeOffset(2026, 10, 29, 6, 17, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("17 */6 * *")]
    [InlineData("43 7 * * 1")]
    [InlineData("0 0 1 * *")]
    [InlineData("60 * * * *")]
    [InlineData("0 24 * * *")]
    [InlineData("*/0 * * * *")]
    [InlineData("5-1 * * * *")]
    [InlineData("a * * * *")]
    [InlineData("-1 * * * *")]
    [InlineData("1/2/3 * * * *")]
    [InlineData("@hourly")]
    public void Unsupported_or_invalid_schedules_give_no_slot(string? schedule)
    {
        CronSlot.Previous(schedule, DateTimeOffset.UnixEpoch).ShouldBeNull();
    }
}
