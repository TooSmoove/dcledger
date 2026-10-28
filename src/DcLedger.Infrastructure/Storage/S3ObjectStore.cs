using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DcLedger.Application;

namespace DcLedger.Infrastructure.Storage;

/// <summary>
/// <see cref="IObjectStore"/> over the S3 API (Cloudflare R2). Retries are the SDK's own; anything that still fails
/// becomes a <see cref="StorageFailedException"/> (ADR-0005).
/// </summary>
internal sealed class S3ObjectStore(IAmazonS3 s3, string bucket) : IObjectStore
{
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) =>
        GuardAsync(key, "check", async () =>
        {
            try
            {
                var request = new GetObjectMetadataRequest { BucketName = bucket, Key = key };
                await s3.GetObjectMetadataAsync(request, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }, cancellationToken);

    public Task<byte[]?> GetAsync(string key, int maxBytes, CancellationToken cancellationToken) =>
        GuardAsync<byte[]?>(key, "read", async () =>
        {
            GetObjectResponse response;
            try
            {
                response = await s3.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key }, cancellationToken).ConfigureAwait(false);
            }
            catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            using (response)
            {
                return await ReadCappedAsync(key, response.ResponseStream, maxBytes, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);

    public Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) =>
        GuardAsync(key, "write", async () =>
        {
            var body = AsStream(content);
            await using (body.ConfigureAwait(false))
            {
                await s3.PutObjectAsync(CreatePutRequest(bucket, key, body, contentType), cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);

    /// <summary>The PUT exactly as spike S2/S3 proved it against R2: unsigned payload, no flexible checksums.</summary>
    internal static PutObjectRequest CreatePutRequest(string bucket, string key, Stream body, string contentType) => new()
    {
        BucketName = bucket,
        Key = key,
        InputStream = body,
        AutoCloseStream = false,
        ContentType = contentType,
        DisablePayloadSigning = true,
    };

    /// <summary>Reads at most <paramref name="maxBytes"/>; Content-Length isn't trusted on its own.</summary>
    private static async Task<byte[]> ReadCappedAsync(string key, Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new StorageFailedException(key, string.Create(CultureInfo.InvariantCulture, $"{key} is larger than {maxBytes} bytes."));
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static MemoryStream AsStream(ReadOnlyMemory<byte> content) =>
        MemoryMarshal.TryGetArray(content, out var segment) && segment.Array is not null
            ? new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(content.ToArray(), writable: false);

    private static async Task GuardAsync(string key, string action, Func<Task> operation, CancellationToken cancellationToken) =>
        await GuardAsync(key, action, async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

    private static async Task<T> GuardAsync<T>(string key, string action, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (AmazonServiceException exception)
        {
            throw Failed(key, action, string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)exception.StatusCode} {exception.ErrorCode}"), exception);
        }
        catch (AmazonClientException exception)
        {
            throw Failed(key, action, exception.Message, exception);
        }
        catch (HttpRequestException exception)
        {
            throw Failed(key, action, exception.Message, exception);
        }
        catch (IOException exception)
        {
            throw Failed(key, action, exception.Message, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failed(key, action, "timed out", exception);
        }
    }

    private static StorageFailedException Failed(string key, string action, string reason, Exception inner) =>
        new(key, $"Couldn't {action} {key} in the private bucket: {reason}", inner);
}
