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

    /// <summary>Validates <paramref name="dataDirectory"/>; <paramref name="previousRecordFiles"/> are record files that must still exist.</summary>
    public ValidationReport Run(string dataDirectory, IReadOnlyCollection<string> previousRecordFiles)
    {
        var snapshot = _reader.Load(dataDirectory);
        var errors = LedgerValidator.Validate(snapshot, previousRecordFiles)
            .OrderBy(e => e.File, StringComparer.Ordinal)
            .ThenBy(e => e.Rule, StringComparer.Ordinal)
            .ToList();
        return new ValidationReport(snapshot.RecordFiles.Count, errors);
    }
}
