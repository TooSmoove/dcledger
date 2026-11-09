using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>What <see cref="ArchiveDocument"/> should keep.</summary>
/// <param name="Url">The public https URL to retrieve, for example a platform's action-detail page or a news story.</param>
/// <param name="SourceType">Who published it; decides whether it can back a <c>confirmed_official</c> label (SPEC AC6).</param>
/// <param name="DryRun">Retrieve and hash only; store nothing.</param>
/// <param name="Copy">
/// A copy a person saved from <paramref name="Url"/> in a browser, for sources that serve only browsers (issue #101). When
/// given, nothing is fetched and the record says <see cref="Retrieval.Manual"/>.
/// </param>
public sealed record ArchiveRequest(Uri Url, SourceType SourceType, bool DryRun, SavedCopy? Copy = null);

/// <summary>A document a person saved in a browser (issue #101).</summary>
/// <param name="Content">The file's bytes, exactly as saved.</param>
/// <param name="FileName">The file's name, used only to guess the media type of non-PDF files.</param>
/// <param name="SavedAtUtc">When the browser saved it (the file's last-modified time); recorded as the retrieval time.</param>
public sealed record SavedCopy(ReadOnlyMemory<byte> Content, string FileName, DateTimeOffset SavedAtUtc);

/// <summary>The outcome of <see cref="ArchiveDocument.RunAsync"/>.</summary>
/// <param name="Document">The ledger record for the retrieved bytes, when retrieval succeeded.</param>
/// <param name="Created">True when this run wrote the bytes; false when the bucket already had them, or in a dry run.</param>
/// <param name="Error">Why it failed, in words safe for a public log; null on success.</param>
public sealed record ArchiveResult(Document? Document, bool Created, string? Error)
{
    /// <summary>True when the document was retrieved and, unless a dry run, stored.</summary>
    public bool Succeeded => Error is null;
}

/// <summary>
/// Retrieves one document by hand and keeps its bytes in the private bucket under their SHA-256, the same way ingest keeps
/// agendas (ADR-0002, ADR-0003), so a manually entered record has the provenance SPEC AC2 asks for. Used for sources
/// ingest doesn't read, such as a decision's action-detail page or a news story (issue #25), or from a copy a person saved
/// in a browser when the source blocks programs (issue #101). Fetch and storage failures are results, not exceptions
/// (ADR-0005).
/// </summary>
public sealed class ArchiveDocument(IFetcher fetcher, IDocumentStore store)
{
    private const string UnknownMediaType = "application/octet-stream";

    /// <summary>Retrieves, hashes and (unless a dry run) stores the document.</summary>
    public async Task<ArchiveResult> RunAsync(ArchiveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Url.Scheme != Uri.UriSchemeHttps)
        {
            return new ArchiveResult(null, false, "Only https URLs can be archived.");
        }

        FetchedResource fetched;
        if (request.Copy is { } copy)
        {
            if (copy.Content.IsEmpty)
            {
                return new ArchiveResult(null, false, "The file is empty.");
            }

            fetched = new FetchedResource(
                copy.Content, MediaTypeGuess.Of(copy.Content.Span, copy.FileName), request.Url, copy.SavedAtUtc, Sha256Hash.Compute(copy.Content.Span));
        }
        else
        {
            try
            {
                fetched = await fetcher.GetAsync(request.Url, cancellationToken).ConfigureAwait(false);
            }
            catch (FetchFailedException exception)
            {
                return new ArchiveResult(null, false, $"Retrieval failed ({exception.Failure}): {exception.Message}");
            }
        }

        var created = false;
        if (!request.DryRun)
        {
            try
            {
                var stored = await store.PutAsync(fetched.Content, fetched.MediaType, cancellationToken).ConfigureAwait(false);
                created = stored.Created;
            }
            catch (StorageFailedException exception)
            {
                return new ArchiveResult(null, false, $"The private bucket couldn't store the document: {exception.Message}");
            }
        }

        var document = new Document
        {
            Id = fetched.Sha256,
            Url = fetched.FinalUri,
            RetrievedAtUtc = TruncateToSecond(fetched.RetrievedAtUtc),
            MediaType = string.IsNullOrWhiteSpace(fetched.MediaType) ? UnknownMediaType : fetched.MediaType,
            Bytes = fetched.Content.Length,
            SourceType = request.SourceType,
            Retrieval = request.Copy is null ? Retrieval.Fetched : Retrieval.Manual,
        };
        return new ArchiveResult(document, created, null);
    }

    // The ledger keeps retrieval times to the second; the record must say the same thing the YAML does.
    private static DateTimeOffset TruncateToSecond(DateTimeOffset value) =>
        new(value.UtcDateTime.Ticks - (value.UtcDateTime.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
}
