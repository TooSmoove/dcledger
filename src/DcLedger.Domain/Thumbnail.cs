namespace DcLedger.Domain;

/// <summary>
/// A picture of a document's first page (issue #132), rendered from the archived bytes whose SHA-256 is the document's
/// ID (<c>tools/render_thumbnail.py</c>) and kept in git as <c>data/thumbnails/{document id}.webp</c>. Its own SHA-256
/// ties the record to that file, so a reviewed picture can't be swapped for another. A person checks it for private
/// individuals' names before it is published (SPEC §E) and says so with <see cref="Reviewed"/>; the quality gate
/// rejects a picture without that mark, one of a document that may not be shown, and one with no record.
/// </summary>
public sealed record Thumbnail
{
    /// <summary>The widest picture accepted, in pixels. Pages show it at half this width or less.</summary>
    public const int MaxWidth = 480;

    /// <summary>The tallest picture accepted, in pixels: a legal-size page at <see cref="MaxWidth"/>.</summary>
    public const int MaxHeight = 820;

    /// <summary>The largest file accepted, in bytes.</summary>
    public const int MaxBytes = 100 * 1024;

    /// <summary>SHA-256 of the WebP file.</summary>
    public required Sha256Hash Sha256 { get; init; }

    /// <summary>Width in pixels, written on the page so the picture reserves its space before it loads.</summary>
    public required int Width { get; init; }

    /// <summary>Height in pixels; see <see cref="Width"/>.</summary>
    public required int Height { get; init; }

    /// <summary>True once a person has checked the picture for private individuals' names (SPEC §E).</summary>
    public required bool Reviewed { get; init; }

    /// <summary>
    /// True for the documents whose first page may be shown: official minutes and legislation pages. Agenda packets
    /// stay private (SPEC §E); other kinds, news and documents of unknown kind are never shown.
    /// </summary>
    public static bool MayShow(DocumentKind? kind, SourceType sourceType) =>
        sourceType == SourceType.Official && kind is DocumentKind.Minutes or DocumentKind.Legislation;
}
