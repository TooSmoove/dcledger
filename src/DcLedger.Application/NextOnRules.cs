using System.Globalization;
using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>
/// The quality gate's check on an action's <c>next_on</c> (ADR-0010 Decision 2). The reader checks that it is a date;
/// this checks it against the decision. Whether the archived document states it stays the reviewer's job
/// (data/README.md). Unlike the terms, it is allowed on any outcome: a continuance names the meeting it goes to.
/// </summary>
internal static class NextOnRules
{
    /// <summary>Adds a problem when <paramref name="action"/>'s next step is not after its decision.</summary>
    public static void Check(List<ValidationError> errors, string file, LocalAction action)
    {
        if (action.NextOn is { } nextOn && nextOn <= action.DecidedOn)
        {
            errors.Add(new ValidationError(file, action.Id, ValidationRule.InvalidValue,
                string.Create(CultureInfo.InvariantCulture,
                    $"next_on {nextOn:yyyy-MM-dd} must be after decided_on {action.DecidedOn:yyyy-MM-dd}: it is the date a document gives for the body's next step.")));
        }
    }
}
