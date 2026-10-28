namespace DcLedger.Domain;

/// <summary>Why an Eastern wall-clock time can't be turned into a single instant.</summary>
public enum WallTimeIssue
{
    /// <summary>The wall time maps to exactly one instant.</summary>
    None,

    /// <summary>The clock jumps over this time when daylight saving starts (2:00–2:59 on the second Sunday of March).</summary>
    SkippedByDaylightSaving,

    /// <summary>The clock passes this time twice when daylight saving ends (1:00–1:59 on the first Sunday of November).</summary>
    RepeatedByDaylightSaving,
}
