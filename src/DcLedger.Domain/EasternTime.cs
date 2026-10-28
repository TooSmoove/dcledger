namespace DcLedger.Domain;

/// <summary>
/// Conversions for America/New_York, the only time zone the ledger covers. Meeting times that fall in a
/// daylight-saving gap or overlap are rejected instead of guessed (docs/spikes/2026-10-25-week1.md, S6).
/// </summary>
public static class EasternTime
{
    /// <summary>The IANA zone; resolves on Windows and Linux alike on .NET 10.</summary>
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>Says whether <paramref name="wallTime"/> (its Kind is ignored) maps to exactly one instant.</summary>
    public static WallTimeIssue Check(DateTime wallTime)
    {
        var local = DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(local))
        {
            return WallTimeIssue.SkippedByDaylightSaving;
        }

        return Zone.IsAmbiguousTime(local) ? WallTimeIssue.RepeatedByDaylightSaving : WallTimeIssue.None;
    }

    /// <summary>Turns an Eastern wall time into an instant carrying the Eastern offset, unless it is skipped or repeated.</summary>
    public static bool TryToInstant(DateTime wallTime, out DateTimeOffset instant, out WallTimeIssue issue)
    {
        issue = Check(wallTime);
        if (issue != WallTimeIssue.None)
        {
            instant = default;
            return false;
        }

        var local = DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified);
        instant = new DateTimeOffset(local, Zone.GetUtcOffset(local));
        return true;
    }

    /// <summary>Expresses <paramref name="instant"/> in Eastern time, with the offset in effect at that instant.</summary>
    public static DateTimeOffset ToEastern(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    /// <summary>True when <paramref name="value"/> carries the Eastern offset in effect at its own instant.</summary>
    public static bool HasEasternOffset(DateTimeOffset value) => value.Offset == Zone.GetUtcOffset(value);
}
