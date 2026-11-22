using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Publishing.Ics;

/// <summary>
/// Writes <c>public/ahead.ics</c> with every known date and <c>public/ahead/{jurisdiction}.ics</c> for each
/// <c>watched</c> or <c>decisions</c> jurisdiction (ADR-0010 Decision 4), so decisions-only jurisdictions get a calendar
/// too. A jurisdiction with no known dates still gets a valid calendar, so people can subscribe before its first one.
/// Kept apart from the radar calendars: those mean "meetings we watch", these are all-day dates the record states.
/// </summary>
public sealed class AheadCalendarExporter : IExporter
{
    /// <summary>The calendar name shown in calendar apps; jurisdiction calendars append the jurisdiction's name.</summary>
    public const string CalendarName = "dc-ledger Coming up on the record";

    private readonly Uri _siteUrl;

    /// <summary>Creates the exporter for the site served at <paramref name="siteUrl"/>; descriptions link back to it.</summary>
    public AheadCalendarExporter(Uri siteUrl)
    {
        ArgumentNullException.ThrowIfNull(siteUrl);
        if (!siteUrl.IsAbsoluteUri || (siteUrl.Scheme != Uri.UriSchemeHttps && siteUrl.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("The site URL must be an absolute http(s) URL.", nameof(siteUrl));
        }

        _siteUrl = siteUrl;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExportFile> Export(ExportModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var files = new List<ExportFile> { new(ExportPaths.Ahead, AheadCalendarWriter.Write(_siteUrl, CalendarName, model.KnownDates)) };
        foreach (var item in model.Jurisdictions.Where(j => j.Jurisdiction.CoverageTier is CoverageTier.Watched or CoverageTier.Decisions))
        {
            var id = item.Jurisdiction.Id;
            var dates = model.KnownDates.Where(known => string.Equals(known.Date.JurisdictionId, id, StringComparison.Ordinal));
            files.Add(new ExportFile(ExportPaths.JurisdictionAhead(id), AheadCalendarWriter.Write(_siteUrl, $"{CalendarName}: {item.Jurisdiction.Name}", dates)));
        }

        return files;
    }
}
