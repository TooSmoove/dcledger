using System.Globalization;
using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>
/// The quality gate's meaning checks (SPEC AC2, AC3, AC6, AC8; WS-AC5). Pure: returns every problem, never throws
/// for bad data (ADR-0005). Shape and format problems come from the reader and are passed through first.
/// </summary>
public static class LedgerValidator
{
    /// <summary>
    /// Validates <paramref name="snapshot"/>. <paramref name="previousRecordFiles"/> lists record files that existed on
    /// the base branch; each must still exist, because published IDs are permanent.
    /// </summary>
    public static IReadOnlyList<ValidationError> Validate(LedgerSnapshot snapshot, IReadOnlyCollection<string> previousRecordFiles)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(previousRecordFiles);

        var errors = new List<ValidationError>(snapshot.LoadErrors);
        var jurisdictionIds = snapshot.Jurisdictions.Select(j => j.Record.Id).ToHashSet(StringComparer.Ordinal);
        var documents = new Dictionary<Sha256Hash, Document>();
        foreach (var (_, document) in snapshot.Documents)
        {
            documents.TryAdd(document.Id, document); // a duplicate also fails ID_MISMATCH below
        }

        foreach (var (file, jurisdiction) in snapshot.Jurisdictions)
        {
            CheckId(errors, file, jurisdiction.Id);
            CheckPlatform(errors, file, jurisdiction);
        }

        foreach (var (file, document) in snapshot.Documents)
        {
            CheckId(errors, file, document.Id.Value);
        }

        foreach (var (file, ledgerEvent) in snapshot.Events)
        {
            CheckId(errors, file, ledgerEvent.Id);
            CheckJurisdiction(errors, file, ledgerEvent.Id, ledgerEvent.JurisdictionId, jurisdictionIds);
            CheckEvidence(errors, file, ledgerEvent.Id, ledgerEvent.DocumentIds, ledgerEvent.Label, documents);
            CheckStartTime(errors, file, ledgerEvent);
            CheckLocation(errors, file, ledgerEvent);
        }

        foreach (var (file, action) in snapshot.Actions)
        {
            CheckId(errors, file, action.Id);
            CheckJurisdiction(errors, file, action.Id, action.JurisdictionId, jurisdictionIds);
            CheckEvidence(errors, file, action.Id, action.DocumentIds, action.Label, documents);
        }

        var present = snapshot.RecordFiles.ToHashSet(StringComparer.Ordinal);
        foreach (var previous in previousRecordFiles.Where(p => !present.Contains(p)).Order(StringComparer.Ordinal))
        {
            errors.Add(new ValidationError(previous, null, ValidationRule.SlugChanged,
                "This record existed before and was removed or renamed. Published IDs are permanent; mark an event cancelled instead."));
        }

        return errors;
    }

    private static void CheckId(List<ValidationError> errors, string file, string id)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        if (!string.Equals(stem, id, StringComparison.Ordinal))
        {
            errors.Add(new ValidationError(file, id, ValidationRule.IdMismatch, $"id '{id}' must equal the file name '{stem}'."));
        }
    }

    private static void CheckPlatform(List<ValidationError> errors, string file, Jurisdiction jurisdiction)
    {
        var isLegistar = jurisdiction.Platform == Platform.Legistar;
        if (isLegistar && (string.IsNullOrWhiteSpace(jurisdiction.LegistarClient) || jurisdiction.Bodies.Count == 0))
        {
            errors.Add(new ValidationError(file, jurisdiction.Id, ValidationRule.InvalidValue,
                "A Legistar jurisdiction needs legistar_client and at least one body."));
        }

        if (!isLegistar && jurisdiction.LegistarClient is not null)
        {
            errors.Add(new ValidationError(file, jurisdiction.Id, ValidationRule.InvalidValue,
                "legistar_client is only allowed when platform is legistar."));
        }
    }

    private static void CheckJurisdiction(List<ValidationError> errors, string file, string id, string jurisdictionId, HashSet<string> jurisdictionIds)
    {
        if (!jurisdictionIds.Contains(jurisdictionId))
        {
            errors.Add(new ValidationError(file, id, ValidationRule.FkMissing,
                $"jurisdiction '{jurisdictionId}' is missing or failed to load."));
        }
    }

    private static void CheckEvidence(
        List<ValidationError> errors,
        string file,
        string id,
        IReadOnlyList<Sha256Hash> documentIds,
        EvidenceLabel label,
        Dictionary<Sha256Hash, Document> documents)
    {
        if (documentIds.Count == 0)
        {
            errors.Add(new ValidationError(file, id, ValidationRule.DocMissing, "Every public item needs at least one document (SPEC AC2)."));
            return;
        }

        var found = new List<Document>();
        foreach (var documentId in documentIds)
        {
            if (documents.TryGetValue(documentId, out var document))
            {
                found.Add(document);
            }
            else
            {
                errors.Add(new ValidationError(file, id, ValidationRule.DocMissing,
                    $"document '{documentId}' is missing or failed to load."));
            }
        }

        if (label == EvidenceLabel.ConfirmedOfficial && !found.Exists(d => d.SourceType == SourceType.Official))
        {
            errors.Add(new ValidationError(file, id, ValidationRule.LabelInvalid,
                "confirmed_official needs at least one document whose source_type is official (SPEC AC6)."));
        }
    }

    private static void CheckStartTime(List<ValidationError> errors, string file, LedgerEvent ledgerEvent)
    {
        if (!EasternTime.HasEasternOffset(ledgerEvent.StartsAt))
        {
            var expected = EasternTime.ToEastern(ledgerEvent.StartsAt).Offset;
            errors.Add(new ValidationError(file, ledgerEvent.Id, ValidationRule.OffsetNotEastern,
                string.Create(CultureInfo.InvariantCulture, $"starts_at offset {ledgerEvent.StartsAt:zzz} isn't the Eastern offset at that time ({FormatOffset(expected)}).")));
        }

        var issue = EasternTime.Check(ledgerEvent.StartsAt.DateTime);
        if (issue != WallTimeIssue.None)
        {
            errors.Add(new ValidationError(file, ledgerEvent.Id, ValidationRule.DstGapOrOverlap,
                string.Create(CultureInfo.InvariantCulture, $"starts_at wall time {ledgerEvent.StartsAt:yyyy-MM-dd HH:mm} is {Describe(issue)} by daylight saving; calendar feeds can't represent it unambiguously.")));
        }
    }

    private static void CheckLocation(List<ValidationError> errors, string file, LedgerEvent ledgerEvent)
    {
        if (ledgerEvent.Location is { IsInNorthCarolina: false } location)
        {
            errors.Add(new ValidationError(file, ledgerEvent.Id, ValidationRule.LocationOutOfRange,
                string.Create(CultureInfo.InvariantCulture,
                    $"location {location.Latitude}, {location.Longitude} is outside North Carolina (latitude {Location.MinLatitude} to {Location.MaxLatitude}, longitude {Location.MinLongitude} to {Location.MaxLongitude}). Is lat/lon swapped, or the longitude's minus sign missing?")));
        }
    }

    private static string Describe(WallTimeIssue issue) =>
        issue == WallTimeIssue.SkippedByDaylightSaving ? "skipped" : "repeated";

    private static string FormatOffset(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
}
