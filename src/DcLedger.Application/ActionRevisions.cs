using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>
/// Compares local actions with their version on the base branch (issue #90), like <see cref="EventRevisions"/>: the
/// feed gives each revision of a decision its own item, keyed by <c>sequence</c>, so a published change must raise it.
/// A decision belongs to its jurisdiction for good (<see cref="ValidationRule.JurisdictionChanged"/>).
/// </summary>
public static class ActionRevisions
{
    /// <summary>Problems with actions present in both, matched by ID; new and removed actions are left to other rules.</summary>
    public static IReadOnlyList<ValidationError> Check(IEnumerable<LoadedRecord<LocalAction>> current, IEnumerable<LoadedRecord<LocalAction>> previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);
        var before = new Dictionary<string, LocalAction>(StringComparer.Ordinal);
        foreach (var (_, action) in previous)
        {
            before.TryAdd(action.Id, action);
        }

        var errors = new List<ValidationError>();
        foreach (var (file, now) in current)
        {
            if (!before.TryGetValue(now.Id, out var was))
            {
                continue;
            }

            if (!string.Equals(was.JurisdictionId, now.JurisdictionId, StringComparison.Ordinal))
            {
                errors.Add(new ValidationError(file, now.Id, ValidationRule.JurisdictionChanged,
                    "jurisdiction_id must keep its published value. A decision belongs to its jurisdiction; record another jurisdiction's decision as a new action."));
                continue;
            }

            Revisions.CheckSequence(errors, file, now.Id, was.Sequence, now.Sequence, ChangedFields(was, now), "feed readers see the change");
        }

        return errors;
    }

    /// <summary>The published fields that differ, by their YAML keys, in a fixed order.</summary>
    public static IReadOnlyList<string> ChangedFields(LocalAction was, LocalAction now)
    {
        ArgumentNullException.ThrowIfNull(was);
        ArgumentNullException.ThrowIfNull(now);
        var changed = new List<string>();
        Revisions.Note(changed, "event_id", !string.Equals(was.EventId, now.EventId, StringComparison.Ordinal));
        Revisions.Note(changed, "action_type", was.ActionType != now.ActionType);
        Revisions.Note(changed, "outcome", was.Outcome != now.Outcome);
        Revisions.Note(changed, "decided_on", was.DecidedOn != now.DecidedOn);
        Revisions.Note(changed, "votes", !string.Equals(was.Votes, now.Votes, StringComparison.Ordinal));
        Revisions.Note(changed, "unanimous", was.Unanimous != now.Unanimous);
        Revisions.Note(changed, "label", was.Label != now.Label);
        Revisions.Note(changed, "document_ids", !was.DocumentIds.SequenceEqual(now.DocumentIds));
        NoteTerms(changed, was.Terms, now.Terms);
        return changed;
    }

    /// <summary>A decision's terms and status are published on its page and in the downloads (issue #115), so each is a revision.</summary>
    private static void NoteTerms(List<string> changed, DecisionTerms was, DecisionTerms now)
    {
        Revisions.Note(changed, "instrument_number", !string.Equals(was.InstrumentNumber, now.InstrumentNumber, StringComparison.Ordinal));
        Revisions.Note(changed, "threshold", !string.Equals(was.Threshold, now.Threshold, StringComparison.Ordinal));
        Revisions.Note(changed, "scope", !string.Equals(was.Scope, now.Scope, StringComparison.Ordinal));
        Revisions.Note(changed, "effective_on", was.EffectiveOn != now.EffectiveOn);
        Revisions.Note(changed, "expires_on", was.ExpiresOn != now.ExpiresOn);
        Revisions.Note(changed, "early_termination", !string.Equals(was.EarlyTermination, now.EarlyTermination, StringComparison.Ordinal));
        Revisions.Note(changed, "exemptions", !string.Equals(was.Exemptions, now.Exemptions, StringComparison.Ordinal));
        Revisions.Note(changed, "successor_id", !string.Equals(was.SuccessorId, now.SuccessorId, StringComparison.Ordinal));
        Revisions.Note(changed, "status", was.Status != now.Status);
    }
}
