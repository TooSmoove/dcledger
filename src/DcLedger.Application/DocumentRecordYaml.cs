using System.Globalization;
using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>
/// The ready-to-paste <c>data/documents/&lt;sha256&gt;.yaml</c> record for a retrieved document, written the same way
/// wherever it is offered (a review issue, <c>dcl archive-document</c>). Every source-derived value is YAML-quoted.
/// </summary>
public static class DocumentRecordYaml
{
    private const string Utc = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>The record's path in the repository.</summary>
    public static string PathFor(Sha256Hash id) => $"data/documents/{id.Value}.yaml";

    /// <summary>The record's lines for <paramref name="document"/>, without a trailing newline.</summary>
    public static IReadOnlyList<string> Lines(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Lines(document.Id, document.Url, document.RetrievedAtUtc, document.MediaType, document.Bytes, document.SourceType, document.Retrieval);
    }

    /// <summary>
    /// The record's lines, without a trailing newline. The retrieval time is written to the second, in UTC.
    /// <c>retrieval</c> is written only when it is <see cref="Retrieval.Manual"/>; left out, it means fetched.
    /// </summary>
    public static IReadOnlyList<string> Lines(
        Sha256Hash id, Uri url, DateTimeOffset retrievedAtUtc, string mediaType, long bytes, SourceType sourceType, Retrieval retrieval = Retrieval.Fetched)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(mediaType);
        List<string> lines =
        [
            $"id: {id.Value}",
            $"url: {IssueText.YamlQuoted(url.AbsoluteUri)}",
            $"retrieved_at: {retrievedAtUtc.UtcDateTime.ToString(Utc, CultureInfo.InvariantCulture)}",
            $"media_type: {IssueText.YamlQuoted(mediaType)}",
            $"bytes: {bytes.ToString(CultureInfo.InvariantCulture)}",
            $"source_type: {sourceType.ToToken()}",
        ];
        if (retrieval == Retrieval.Manual)
        {
            lines.Add($"retrieval: {retrieval.ToToken()}");
        }

        return lines;
    }
}
