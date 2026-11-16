namespace DcLedger.Publishing;

/// <summary>
/// Where each export goes, relative to <c>dcl export --out</c>. <c>site-data/</c> is what the site reads at build time
/// (ADR-0001); <c>public/</c> holds files the site serves as they are, at the same paths under its root.
/// </summary>
public static class ExportPaths
{
    /// <summary>The site's data contract (ADR-0004).</summary>
    public const string SiteData = "site-data/events.json";

    /// <summary>
    /// The corrections log for the methodology page (issue #117). Site data only: not copied to the public downloads
    /// and not part of the frozen data contract (docs/export.md).
    /// </summary>
    public const string SiteCorrections = "site-data/corrections.json";

    /// <summary>Every jurisdiction's events in one calendar.</summary>
    public const string Radar = "public/radar.ics";

    /// <summary>One row per event.</summary>
    public const string EventsCsv = "public/events.csv";

    /// <summary>The same rows as <see cref="EventsCsv"/>, typed, with each event's documents as a list.</summary>
    public const string EventsParquet = "public/events.parquet";

    /// <summary>One row per local action (decision), issue #90.</summary>
    public const string ActionsCsv = "public/actions.csv";

    /// <summary>The same rows as <see cref="ActionsCsv"/>, typed, with each action's documents as a list.</summary>
    public const string ActionsParquet = "public/actions.parquet";

    /// <summary>The RSS 2.0 feed of reviewed changes.</summary>
    public const string Feed = "public/feed.xml";

    /// <summary>
    /// A document's first-page picture (issue #132), for example <c>public/thumbnails/{sha256}.webp</c>; the site serves
    /// it at <see cref="ThumbnailUrlPath"/>.
    /// </summary>
    public static string Thumbnail(string documentId) => $"public/thumbnails/{documentId}.webp";

    /// <summary>Where the site serves <see cref="Thumbnail"/>: <c>/thumbnails/{sha256}.webp</c>.</summary>
    public static string ThumbnailUrlPath(string documentId) => $"/thumbnails/{documentId}.webp";

    /// <summary>One jurisdiction's calendar, for example <c>public/radar/guilford.ics</c>.</summary>
    public static string JurisdictionRadar(string jurisdictionId) => $"public/radar/{jurisdictionId}.ics";
}
