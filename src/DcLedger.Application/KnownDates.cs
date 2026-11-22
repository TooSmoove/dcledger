using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>A known date with its state at D (ADR-0010 Decision 3).</summary>
public sealed record KnownDateAt(KnownDate Date, KnownDateState State);

/// <summary>
/// Works out every known date the ledger states and where each stands at D (ADR-0010). Pure: the same actions, events
/// and D always give the same list, ordered by date, then kind, then action ID.
/// </summary>
public static class KnownDates
{
    /// <summary>The known dates of <paramref name="actions"/> at <paramref name="today"/>, the Eastern date of the export.</summary>
    public static IReadOnlyList<KnownDateAt> From(IEnumerable<LocalAction> actions, IEnumerable<LedgerEvent> events, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(events);
        var actionList = actions.ToList();

        // What each jurisdiction has on the record by Eastern date: a meeting that day, or a decision taken that day.
        var onRecord = new HashSet<(string JurisdictionId, DateOnly Day)>();
        foreach (var ledgerEvent in events)
        {
            onRecord.Add((ledgerEvent.JurisdictionId, KnownDateRules.EasternDate(ledgerEvent.StartsAt)));
        }

        foreach (var action in actionList)
        {
            onRecord.Add((action.JurisdictionId, action.DecidedOn));
        }

        return
        [
            .. actionList
                .SelectMany(action => KnownDateRules.Of(action).Select(date => new KnownDateAt(
                    date, KnownDateRules.StateAt(date, action, onRecord.Contains((date.JurisdictionId, date.Date)), today))))
                .OrderBy(item => item.Date.Date)
                .ThenBy(item => item.Date.Kind)
                .ThenBy(item => item.Date.ActionId, StringComparer.Ordinal),
        ];
    }
}
