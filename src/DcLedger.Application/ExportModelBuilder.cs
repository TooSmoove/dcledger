using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>The export model plus anything worth telling the operator that doesn't stop the export.</summary>
public sealed record ExportModelResult(ExportModel Model, IReadOnlyList<string> Warnings);

/// <summary>Builds the <see cref="ExportModel"/> from a validated snapshot and the ingest status. Pure.</summary>
public static class ExportModelBuilder
{
    /// <summary>
    /// Resolves and orders the snapshot. "Last checked" comes from <paramref name="status"/> for automated
    /// jurisdictions and from <see cref="Jurisdiction.LastManualCheckUtc"/> for manual ones. Status entries for IDs
    /// that aren't automated jurisdictions are ignored with a warning, so a stale entry in the run history can't stop
    /// publishing. Throws <see cref="InvalidOperationException"/> if the snapshot wasn't validated first.
    /// </summary>
    public static ExportModelResult Build(LedgerSnapshot snapshot, IngestStatus status)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(status);

        var jurisdictions = snapshot.Jurisdictions
            .Select(loaded => loaded.Record)
            .OrderBy(jurisdiction => jurisdiction.Id, StringComparer.Ordinal)
            .Select(jurisdiction => Freshen(jurisdiction, status))
            .ToList();
        var byId = jurisdictions.ToDictionary(j => j.Jurisdiction.Id, j => j.Jurisdiction, StringComparer.Ordinal);
        var documents = snapshot.Documents.ToDictionary(loaded => loaded.Record.Id, loaded => loaded.Record);

        var events = snapshot.Events
            .Select(loaded => loaded.Record)
            .OrderBy(ledgerEvent => ledgerEvent.StartsAt)
            .ThenBy(ledgerEvent => ledgerEvent.Id, StringComparer.Ordinal)
            .Select(ledgerEvent => new ExportEvent
            {
                Event = ledgerEvent,
                Jurisdiction = Lookup(byId, ledgerEvent.JurisdictionId, ledgerEvent.Id),
                Documents = [.. ledgerEvent.DocumentIds.Select(id => Lookup(documents, id, ledgerEvent.Id))],
            })
            .ToList();

        var model = new ExportModel { GeneratedUtc = status.GeneratedUtc, Jurisdictions = jurisdictions, Events = events };
        return new ExportModelResult(model, Warnings(jurisdictions, status));
    }

    private static ExportJurisdiction Freshen(Jurisdiction jurisdiction, IngestStatus status)
    {
        var lastChecked = jurisdiction.Platform == Platform.Manual
            ? jurisdiction.LastManualCheckUtc
            : status.LastCheckedUtc.GetValueOrDefault(jurisdiction.Id);
        return new ExportJurisdiction
        {
            Jurisdiction = jurisdiction,
            LastCheckedUtc = lastChecked,
            Stale = Freshness.IsStale(lastChecked, status.GeneratedUtc),
        };
    }

    private static List<string> Warnings(List<ExportJurisdiction> jurisdictions, IngestStatus status)
    {
        var automated = jurisdictions
            .Where(j => j.Jurisdiction.Platform != Platform.Manual)
            .Select(j => j.Jurisdiction.Id)
            .ToHashSet(StringComparer.Ordinal);
        return
        [
            .. status.LastCheckedUtc.Keys
                .Where(id => !automated.Contains(id))
                .Order(StringComparer.Ordinal)
                .Select(id => $"The ingest status lists '{id}', which isn't an automated jurisdiction in the ledger; ignored."),
        ];
    }

    private static TValue Lookup<TKey, TValue>(Dictionary<TKey, TValue> values, TKey key, string eventId)
        where TKey : notnull =>
        values.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Event '{eventId}' references '{key}', which isn't loaded. Validate the ledger before exporting.");
}
