using System.Globalization;
using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>
/// The quality gate's checks on a decision's terms (issue #115). Format problems (length, dates, tokens) come from the
/// reader; these are the rules that need the rest of the record or another action. Whether a term matches the archived
/// document stays the reviewer's job (data/README.md).
/// </summary>
internal static class DecisionTermsRules
{
    /// <summary>Adds every problem with <paramref name="action"/>'s terms. <paramref name="actions"/> holds every loaded action by ID.</summary>
    public static void Check(List<ValidationError> errors, string file, LocalAction action, IReadOnlyDictionary<string, LocalAction> actions)
    {
        var terms = action.Terms;
        if (terms.IsEmpty)
        {
            return;
        }

        if (action.Outcome is not (ActionOutcome.Adopted or ActionOutcome.AdoptedAmended))
        {
            errors.Add(Invalid(file, action,
                $"terms and status describe an adopted decision, but outcome is {action.Outcome.ToToken()}. Remove them, or record the adoption as its own action."));
        }

        if (terms.EffectiveOn is { } effectiveOn && terms.ExpiresOn is { } expiresOn && expiresOn < effectiveOn)
        {
            errors.Add(Invalid(file, action,
                string.Create(CultureInfo.InvariantCulture, $"expires_on {expiresOn:yyyy-MM-dd} is before effective_on {effectiveOn:yyyy-MM-dd}.")));
        }

        if (terms.Status == ActionStatus.Superseded && terms.SuccessorId is null)
        {
            errors.Add(Invalid(file, action, "status superseded needs successor_id: the action that replaced or extended this one."));
        }

        if (terms.SuccessorId is { } successorId)
        {
            CheckSuccessor(errors, file, action, successorId, actions);
        }
    }

    private static void CheckSuccessor(List<ValidationError> errors, string file, LocalAction action, string successorId, IReadOnlyDictionary<string, LocalAction> actions)
    {
        if (string.Equals(successorId, action.Id, StringComparison.Ordinal))
        {
            errors.Add(Invalid(file, action, "successor_id names this action itself."));
            return;
        }

        if (!actions.TryGetValue(successorId, out var successor))
        {
            errors.Add(new ValidationError(file, action.Id, ValidationRule.FkMissing, $"successor action '{successorId}' is missing or failed to load."));
            return;
        }

        if (!string.Equals(successor.JurisdictionId, action.JurisdictionId, StringComparison.Ordinal))
        {
            errors.Add(Invalid(file, action,
                $"successor '{successor.Id}' belongs to jurisdiction '{successor.JurisdictionId}', not '{action.JurisdictionId}'."));
        }

        if (successor.DecidedOn < action.DecidedOn)
        {
            errors.Add(Invalid(file, action,
                string.Create(CultureInfo.InvariantCulture, $"successor '{successor.Id}' was decided on {successor.DecidedOn:yyyy-MM-dd}, before this action ({action.DecidedOn:yyyy-MM-dd}).")));
        }
    }

    private static ValidationError Invalid(string file, LocalAction action, string message) =>
        new(file, action.Id, ValidationRule.TermsInvalid, message);
}
