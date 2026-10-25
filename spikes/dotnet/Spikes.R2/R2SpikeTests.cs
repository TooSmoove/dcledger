using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Shouldly;
using Xunit;

namespace Spikes.R2;

/// <summary>Spikes S2/S3: AWSSDK.S3 4.x against Cloudflare R2. Skips when secrets are absent.</summary>
public class R2SpikeTests
{
    private const string Body = "dc-ledger spike";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Single_and_multipart_round_trip_on_private_bucket(bool forcePathStyle)
    {
        var (account, bucket, s3) = PrivateClient(forcePathStyle);
        var ct = TestContext.Current.CancellationToken;
        var key = $"spike/{Guid.NewGuid():N}.txt";
        var bigKey = key + ".bin";
        try
        {
            await s3.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = key, ContentBody = Body, DisablePayloadSigning = true }, ct);
            (await s3.GetObjectMetadataAsync(bucket, key, ct)).ContentLength.ShouldBe((long)Body.Length);
            using (var get = await s3.GetObjectAsync(bucket, key, ct))
            using (var reader = new StreamReader(get.ResponseStream))
            {
                (await reader.ReadToEndAsync(ct)).ShouldBe(Body);
            }

            var big = new byte[12 * 1024 * 1024];
            Random.Shared.NextBytes(big);
            await new TransferUtility(s3).UploadAsync(new TransferUtilityUploadRequest
            {
                BucketName = bucket,
                Key = bigKey,
                InputStream = new MemoryStream(big),
                PartSize = 5 * 1024 * 1024,
                DisablePayloadSigning = true,
            }, ct);
            (await s3.GetObjectMetadataAsync(bucket, bigKey, ct)).ContentLength.ShouldBe((long)big.Length);

            using var anonymous = new HttpClient();
            var anon = await anonymous.GetAsync($"https://{account}.r2.cloudflarestorage.com/{bucket}/{key}", ct);
            ((int)anon.StatusCode).ShouldBeOneOf(400, 401, 403);

            Findings.Write("S2/S3", $"AWSSDK.S3 {typeof(AmazonS3Client).Assembly.GetName().Version} forcePathStyle={forcePathStyle}: put/head/get/multipart OK; anonymous GET -> {(int)anon.StatusCode}");
        }
        finally
        {
            await s3.DeleteObjectAsync(bucket, key, CancellationToken.None);
            await s3.DeleteObjectAsync(bucket, bigKey, CancellationToken.None);
            s3.Dispose();
        }
    }

    [Fact]
    public async Task Private_token_cannot_list_public_bucket()
    {
        var publicBucket = Environment.GetEnvironmentVariable("R2_PUBLIC_BUCKET");
        if (string.IsNullOrEmpty(publicBucket))
        {
            Assert.Skip("R2_PUBLIC_BUCKET not configured");
        }

        var (_, _, s3) = PrivateClient(forcePathStyle: false);
        using (s3)
        {
            var ex = await Should.ThrowAsync<AmazonS3Exception>(() =>
                s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = publicBucket, MaxKeys = 1 }, TestContext.Current.CancellationToken));
            Findings.Write("S2/S3", $"private token on public bucket -> {(int)ex.StatusCode} {ex.ErrorCode}");
        }
    }

    private static (string Account, string Bucket, AmazonS3Client Client) PrivateClient(bool forcePathStyle)
    {
        string? Env(string name) => Environment.GetEnvironmentVariable(name);
        var account = Env("R2_ACCOUNT_ID");
        var bucket = Env("R2_PRIVATE_BUCKET");
        var keyId = Env("R2_PRIVATE_ACCESS_KEY_ID");
        var secret = Env("R2_PRIVATE_SECRET_ACCESS_KEY");
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(secret))
        {
            Assert.Skip("R2 secrets not configured");
        }

        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{account}.r2.cloudflarestorage.com",
            AuthenticationRegion = "auto",
            ForcePathStyle = forcePathStyle,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        return (account!, bucket!, new AmazonS3Client(new BasicAWSCredentials(keyId, secret), config));
    }
}
