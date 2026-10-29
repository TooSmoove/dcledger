namespace DcLedger.Publishing;

/// <summary>
/// Where each export goes, relative to <c>dcl export --out</c>. <c>site-data/</c> is what the site reads at build time
/// (ADR-0001); <c>public/</c> holds files the site serves as they are, at the same paths under its root.
/// </summary>
public static class ExportPaths
{
    /// <summary>The site's data contract (ADR-0004).</summary>
    public const string SiteData = "site-data/events.json";

    /// <summary>Every jurisdiction's events in one calendar.</summary>
    public const string Radar = "public/radar.ics";

    /// <summary>One row per event.</summary>
    public const string EventsCsv = "public/events.csv";

    /// <summary>One jurisdiction's calendar, for example <c>public/radar/guilford.ics</c>.</summary>
    public static string JurisdictionRadar(string jurisdictionId) => $"public/radar/{jurisdictionId}.ics";
}
