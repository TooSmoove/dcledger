using DcLedger.Application;

namespace DcLedger.Publishing.Ics;

/// <summary>
/// Writes <c>public/radar.ics</c> with every event and <c>public/radar/{jurisdiction}.ics</c> per jurisdiction (SPEC
/// AC3). A jurisdiction with no events still gets a valid calendar, so people can subscribe before its first item.
/// </summary>
public sealed class RadarCalendarExporter : IExporter
{
    /// <summary>The calendar name shown in calendar apps; jurisdiction calendars append the jurisdiction's name.</summary>
    public const string CalendarName = "dc-ledger Hearing Radar";

    /// <inheritdoc />
    public IReadOnlyList<ExportFile> Export(ExportModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var files = new List<ExportFile> { new(ExportPaths.Radar, IcsCalendarWriter.Write(CalendarName, model.Events)) };
        foreach (var item in model.Jurisdictions)
        {
            var id = item.Jurisdiction.Id;
            var events = model.Events.Where(exportEvent => string.Equals(exportEvent.Event.JurisdictionId, id, StringComparison.Ordinal));
            files.Add(new ExportFile(ExportPaths.JurisdictionRadar(id), IcsCalendarWriter.Write($"{CalendarName}: {item.Jurisdiction.Name}", events)));
        }

        return files;
    }
}
