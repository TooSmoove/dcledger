using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>
/// Compares events with their version on the base branch (issue #29, SPEC AC4). A calendar app applies an update only
/// when its SEQUENCE is higher (RFC 5545 §3.8.7.4), so a published change that keeps <c>sequence</c> would never reach
/// subscribers; and the UID is built from the jurisdiction, platform and meeting ID, so changing any of them would show
/// a second event instead of updating the first. Pure, like <see cref="LedgerValidator"/>.
/// </summary>
public static class EventRevisions
{
    /// <summary>
    /// Problems with events present in both <paramref name="current"/> and <paramref name="previous"/>, matched by ID.
    /// New events and removed ones aren't this check's business (removal is <see cref="ValidationRule.SlugChanged"/>).
    /// </summary>
    public static IReadOnlyList<ValidationError> Check(IEnumerable<LoadedRecord<LedgerEvent>> current, IEnumerable<LoadedRecord<LedgerEvent>> previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);
        var before = new Dictionary<string, LedgerEvent>(StringComparer.Ordinal);
        foreach (var (_, ledgerEvent) in previous)
        {
            before.TryAdd(ledgerEvent.Id, ledgerEvent);
        }

        var errors = new List<ValidationError>();
        foreach (var (file, now) in current)
        {
            if (before.TryGetValue(now.Id, out var was))
            {
                CheckRevision(errors, file, was, now);
            }
        }

        return errors;
    }

    /// <summary>The published fields that differ, by their YAML keys, in a fixed order.</summary>
    public static IReadOnlyList<string> ChangedFields(LedgerEvent was, LedgerEvent now)
    {
        ArgumentNullException.ThrowIfNull(was);
        ArgumentNullException.ThrowIfNull(now);
        var changed = new List<string>();
        Note(changed, "body", !string.Equals(was.Body, now.Body, StringComparison.Ordinal));
        Note(changed, "title", !string.Equals(was.Title, now.Title, StringComparison.Ordinal));
        Note(changed, "case_number", !string.Equals(was.CaseNumber, now.CaseNumber, StringComparison.Ordinal));
        Note(changed, "starts_at", was.StartsAt != now.StartsAt);
        Note(changed, "status", was.Status != now.Status);
        Note(changed, "label", was.Label != now.Label);
        Note(changed, "document_ids", !was.DocumentIds.SequenceEqual(now.DocumentIds));
        Note(changed, "location", was.Location != now.Location);
        return changed;
    }

    private static void CheckRevision(List<ValidationError> errors, string file, LedgerEvent was, LedgerEvent now)
    {
        if (!string.Equals(was.Uid, now.Uid, StringComparison.Ordinal))
        {
            errors.Add(new ValidationError(file, now.Id, ValidationRule.UidChanged,
                "jurisdiction_id, platform and platform_meeting_id must keep their published values: the calendar UID is built from them, "
                + "so subscribers would see a second event. To replace a meeting, cancel this one and add a new record."));
            return;
        }

        Revisions.CheckSequence(errors, file, now.Id, was.Sequence, now.Sequence, ChangedFields(was, now), "calendar apps apply the change");
    }

    private static void Note(List<string> changed, string field, bool differs) => Revisions.Note(changed, field, differs);
}

/// <summary>The sequence rule shared by <see cref="EventRevisions"/> and <see cref="ActionRevisions"/>.</summary>
internal static class Revisions
{
    /// <summary>
    /// <paramref name="now"/> may not be lower than <paramref name="was"/>, and must be higher when any field in
    /// <paramref name="changed"/> differs (<see cref="ValidationRule.SequenceNotRaised"/>).
    /// </summary>
    public static void CheckSequence(List<ValidationError> errors, string file, string id, int was, int now, IReadOnlyList<string> changed, string why)
    {
        if (now < was)
        {
            errors.Add(new ValidationError(file, id, ValidationRule.SequenceNotRaised,
                $"sequence went down from {was} to {now}; it only goes up."));
            return;
        }

        if (changed.Count > 0 && now == was)
        {
            errors.Add(new ValidationError(file, id, ValidationRule.SequenceNotRaised,
                $"{string.Join(", ", changed)} changed but sequence is still {now}. Raise it to {now + 1} so {why}."));
        }
    }

    public static void Note(List<string> changed, string field, bool differs)
    {
        if (differs)
        {
            changed.Add(field);
        }
    }
}
