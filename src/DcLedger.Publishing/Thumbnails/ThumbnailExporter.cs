using DcLedger.Application;

namespace DcLedger.Publishing.Thumbnails;

/// <summary>
/// Copies each exported document's first-page picture (issue #132) to <c>public/thumbnails/{sha256}.webp</c>, byte for
/// byte, so the site serves the reviewed file whose hash the record states. The quality gate has already decided
/// which pictures may be published (<see cref="ThumbnailRules"/>); this only copies them.
/// </summary>
public sealed class ThumbnailExporter : IExporter
{
    /// <inheritdoc />
    public IReadOnlyList<ExportFile> Export(ExportModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return [.. model.Thumbnails.Select(file => new ExportFile(ExportPaths.Thumbnail(file.DocumentId.Value), file.Content))];
    }
}
