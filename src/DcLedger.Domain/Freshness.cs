namespace DcLedger.Domain;

/// <summary>
/// When a jurisdiction's data counts as stale: no successful check for more than 48 hours (SPEC AC10). Applied
/// mechanically at export time, against the time the ingest status was built, so the same inputs give the same answer.
/// </summary>
public static class Freshness
{
    /// <summary>How long a check stays fresh. Exactly 48 hours is still fresh; the flag appears after that.</summary>
    public static TimeSpan StaleAfter { get; } = TimeSpan.FromHours(48);

    /// <summary>True when there has been no successful check, or the last one is more than 48 hours before <paramref name="asOf"/>.</summary>
    public static bool IsStale(DateTimeOffset? lastChecked, DateTimeOffset asOf) =>
        lastChecked is not { } checkedAt || asOf - checkedAt > StaleAfter;
}
