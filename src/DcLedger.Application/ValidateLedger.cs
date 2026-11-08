namespace DcLedger.Application;

/// <summary>The outcome of <see cref="ValidateLedger"/>: how many record files were found and every problem, ordered by file.</summary>
public sealed record ValidationReport(int RecordFiles, IReadOnlyList<ValidationError> Errors)
{
    /// <summary>True when the data passed the quality gate.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Use case behind <c>dcl validate</c>: read the data directory, then run the quality gate.</summary>
public sealed class ValidateLedger(ILedgerReader reader)
{
    private readonly ILedgerReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>
    /// Validates <paramref name="dataDirectory"/>; <paramref name="previousRecordFiles"/> are record files that must still
    /// exist. When <paramref name="previousDataDirectory"/> holds the data as it is on the base branch, changed events and
    /// actions are also checked against it (<see cref="EventRevisions"/>, <see cref="ActionRevisions"/>); problems in the
    /// old data itself are ignored.
    /// </summary>
    public ValidationReport Run(string dataDirectory, IReadOnlyCollection<string> previousRecordFiles, string? previousDataDirectory = null)
    {
        var previous = previousDataDirectory is null ? null : _reader.Load(previousDataDirectory);
        return Check(_reader.Load(dataDirectory), previousRecordFiles, previous);
    }

    /// <summary>
    /// Runs the quality gate on an already-loaded <paramref name="snapshot"/>, and against <paramref name="previous"/>
    /// when given; errors are ordered by file, then rule.
    /// </summary>
    public static ValidationReport Check(LedgerSnapshot snapshot, IReadOnlyCollection<string> previousRecordFiles, LedgerSnapshot? previous = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        IReadOnlyList<ValidationError> revisions = previous is null
            ? []
            : [.. EventRevisions.Check(snapshot.Events, previous.Events), .. ActionRevisions.Check(snapshot.Actions, previous.Actions)];
        var errors = LedgerValidator.Validate(snapshot, previousRecordFiles)
            .Concat(revisions)
            .OrderBy(e => e.File, StringComparer.Ordinal)
            .ThenBy(e => e.Rule, StringComparer.Ordinal)
            .ToList();
        return new ValidationReport(snapshot.RecordFiles.Count, errors);
    }
}
