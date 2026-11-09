namespace DcLedger.Domain;

/// <summary>
/// When a jurisdiction's data counts as stale (SPEC AC10): no successful check for more than 48 hours for an automated
/// jurisdiction, or more than 7 days for one checked by hand (issue #27; #96 aims to automate those and bring it back
/// down). Applied mechanically at export time, against the time the ingest status was built, so the same inputs give
/// the same answer.
/// </summary>
public static class Freshness
{
    /// <summary>How long an automated check stays fresh. Exactly 48 hours is still fresh; the flag appears after that.</summary>
    public static TimeSpan StaleAfter { get; } = TimeSpan.FromHours(48);

    /// <summary>How long a manual check stays fresh: 7 days, since each one is a reviewed commit.</summary>
    public static TimeSpan ManualStaleAfter { get; } = TimeSpan.FromDays(7);

    /// <summary>The threshold for a jurisdiction on <paramref name="platform"/>.</summary>
    public static TimeSpan StaleAfterFor(Platform platform) => platform == Platform.Manual ? ManualStaleAfter : StaleAfter;

    /// <summary>True when there has been no successful check, or the last one is more than 48 hours before <paramref name="asOf"/>.</summary>
    public static bool IsStale(DateTimeOffset? lastChecked, DateTimeOffset asOf) => IsStale(lastChecked, asOf, StaleAfter);

    /// <summary>True when there has been no successful check, or the last one is more than <paramref name="staleAfter"/> before <paramref name="asOf"/>.</summary>
    public static bool IsStale(DateTimeOffset? lastChecked, DateTimeOffset asOf, TimeSpan staleAfter) =>
        lastChecked is not { } checkedAt || asOf - checkedAt > staleAfter;
}
