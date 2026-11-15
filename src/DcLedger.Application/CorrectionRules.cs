using System.Globalization;
using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>
/// The quality gate's checks on one corrections log entry (issue #117). Every record it names must be a jurisdiction,
/// event or action in the ledger (<c>FK_MISSING</c>), so the log never points at nothing. Its dates must be in order
/// and on or after the day the log opened (<c>CORRECTION_INVALID</c>). The validator has no clock, so a future date
/// is caught in review, not here.
/// </summary>
public static class CorrectionRules
{
    /// <summary>Adds every problem with <paramref name="correction"/> to <paramref name="errors"/>.</summary>
    public static void Check(List<ValidationError> errors, string file, Correction correction, IReadOnlySet<string> recordIds)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(correction);
        ArgumentNullException.ThrowIfNull(recordIds);

        if (correction.ReportedOn < Correction.LogOpenedOn)
        {
            Add(errors, file, correction, string.Create(CultureInfo.InvariantCulture,
                $"reported_on {correction.ReportedOn:yyyy-MM-dd} is before the log opened on {Correction.LogOpenedOn:yyyy-MM-dd}; the log covers only what was reported from that day on."));
        }

        if (correction.CorrectedOn < correction.ReportedOn)
        {
            Add(errors, file, correction, string.Create(CultureInfo.InvariantCulture,
                $"corrected_on {correction.CorrectedOn:yyyy-MM-dd} is before reported_on {correction.ReportedOn:yyyy-MM-dd}."));
        }

        if (correction.Issue is <= 0)
        {
            Add(errors, file, correction, "issue must be a GitHub issue number, 1 or more.");
        }

        foreach (var duplicate in correction.RecordIds.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            Add(errors, file, correction, $"record_ids lists '{duplicate.Key}' more than once.");
        }

        foreach (var recordId in correction.RecordIds.Distinct(StringComparer.Ordinal).Where(id => !recordIds.Contains(id)))
        {
            errors.Add(new ValidationError(file, correction.Id, ValidationRule.FkMissing,
                $"record '{recordId}' isn't a jurisdiction, event or action in the ledger."));
        }
    }

    private static void Add(List<ValidationError> errors, string file, Correction correction, string message) =>
        errors.Add(new ValidationError(file, correction.Id, ValidationRule.CorrectionInvalid, message));
}
