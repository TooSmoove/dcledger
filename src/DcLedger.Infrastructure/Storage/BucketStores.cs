using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Infrastructure.Storage;

/// <summary>
/// Documents by SHA-256. Checks for the key first and never rewrites an object. Two runs racing on the same new
/// document both write the same bytes to the same key, which is harmless.
/// </summary>
internal sealed class BucketDocumentStore(IObjectStore bucket) : IDocumentStore
{
    private const string UnknownMediaType = "application/octet-stream";

    public async Task<StoredDocument> PutAsync(ReadOnlyMemory<byte> content, string? mediaType, CancellationToken cancellationToken)
    {
        var sha256 = Sha256Hash.Compute(content.Span);
        var key = StorageKeys.Document(sha256);
        if (await bucket.ExistsAsync(key, cancellationToken).ConfigureAwait(false))
        {
            return new StoredDocument(sha256, key, Created: false);
        }

        var contentType = string.IsNullOrWhiteSpace(mediaType) ? UnknownMediaType : mediaType;
        await bucket.PutAsync(key, content, contentType, cancellationToken).ConfigureAwait(false);
        return new StoredDocument(sha256, key, Created: true);
    }
}

/// <summary>One small JSON object per meeting.</summary>
internal sealed class BucketIngestState(IObjectStore bucket) : IIngestState
{
    private const int MaxStateBytes = 64 * 1024;

    public async Task<MeetingState?> GetAsync(MeetingKey meeting, CancellationToken cancellationToken)
    {
        var key = StorageKeys.MeetingState(meeting);
        var json = await bucket.GetAsync(key, MaxStateBytes, cancellationToken).ConfigureAwait(false);
        return json is null ? null : StorageJson.DeserializeMeetingState(key, json);
    }

    public Task PutAsync(MeetingKey meeting, MeetingState state, CancellationToken cancellationToken) =>
        bucket.PutAsync(StorageKeys.MeetingState(meeting), StorageJson.Serialize(state), "application/json", cancellationToken);
}

/// <summary>One JSON object per run, filed by month.</summary>
internal sealed class BucketRunLog(IObjectStore bucket) : IRunLog
{
    public Task WriteAsync(RunRecord run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        return bucket.PutAsync(StorageKeys.Run(run.StartedUtc, run.RunId), StorageJson.Serialize(run), "application/json", cancellationToken);
    }
}
