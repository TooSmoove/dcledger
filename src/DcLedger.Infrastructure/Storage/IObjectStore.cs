namespace DcLedger.Infrastructure.Storage;

/// <summary>The bucket operations the stores need, so key layout and idempotency are testable without S3.</summary>
internal interface IObjectStore
{
    /// <summary>True when an object exists at <paramref name="key"/>.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);

    /// <summary>The object's bytes, or null when there is none. Objects over <paramref name="maxBytes"/> are refused.</summary>
    Task<byte[]?> GetAsync(string key, int maxBytes, CancellationToken cancellationToken);

    /// <summary>Writes the object, replacing any existing one.</summary>
    Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);
}
