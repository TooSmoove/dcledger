using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>
/// The quality gate's checks on first-page pictures (issue #132). A document's <c>thumbnail</c> is published only when
/// its document may be shown (official minutes or legislation, <see cref="Thumbnail.MayShow"/>), a person marked it
/// reviewed for private individuals' names (SPEC §E), and <c>data/thumbnails/{id}.webp</c> is there with the SHA-256
/// the record states. Every file under <c>data/thumbnails/</c> must belong to such a record, so no picture sits in the
/// ledger unreviewed or unclaimed.
/// </summary>
public static class ThumbnailRules
{
    /// <summary>Adds every problem with the documents' pictures and the picture files to <paramref name="errors"/>.</summary>
    public static void Check(
        List<ValidationError> errors,
        IReadOnlyList<LoadedRecord<Document>> documents,
        IReadOnlyList<ThumbnailFile> files)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(files);

        var byDocument = new Dictionary<Sha256Hash, ThumbnailFile>();
        foreach (var file in files)
        {
            byDocument.TryAdd(file.DocumentId, file);
        }

        var claimed = new HashSet<Sha256Hash>();
        foreach (var (file, document) in documents)
        {
            if (document.Thumbnail is not { } thumbnail)
            {
                continue;
            }

            claimed.Add(document.Id);
            var id = document.Id.Value;
            if (!Thumbnail.MayShow(document.Kind, document.SourceType))
            {
                errors.Add(new ValidationError(file, id, ValidationRule.ThumbnailInvalid,
                    "Only an official document of kind minutes or legislation may show its first page; agenda packets stay private (SPEC §E). Remove thumbnail and its file."));
            }

            if (!thumbnail.Reviewed)
            {
                errors.Add(new ValidationError(file, id, ValidationRule.ThumbnailUnreviewed,
                    "Check the picture for private individuals' names, then set thumbnail.reviewed to true (SPEC §E)."));
            }

            if (!byDocument.TryGetValue(document.Id, out var picture))
            {
                errors.Add(new ValidationError(file, id, ValidationRule.ThumbnailInvalid,
                    $"thumbnails/{id}.webp is missing."));
            }
            else if (picture.ContentHash != thumbnail.Sha256)
            {
                errors.Add(new ValidationError(file, id, ValidationRule.ThumbnailInvalid,
                    $"thumbnails/{id}.webp has SHA-256 {picture.ContentHash}, not the {thumbnail.Sha256} this record states."));
            }
        }

        foreach (var file in files.Where(file => !claimed.Contains(file.DocumentId)))
        {
            errors.Add(new ValidationError(file.File, file.DocumentId.Value, ValidationRule.ThumbnailInvalid,
                "No document record has a thumbnail for this file: add one to documents/ or delete the file."));
        }
    }
}
