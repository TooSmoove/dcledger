using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using DcLedger.Application;
using DcLedger.Infrastructure.Storage;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace DcLedger.Infrastructure.Tests.Storage;

/// <summary>
/// The S3 adapter against a fake endpoint: status codes map to results and failures. PUT isn't exercised here because
/// an unsigned payload requires https; the request it sends is pinned below and was proven live by spike S2/S3.
/// </summary>
public sealed class S3ObjectStoreTests : IDisposable
{
    private const string Bucket = "test-bucket";

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly AmazonS3Client _client;

    public S3ObjectStoreTests()
    {
        _client = new AmazonS3Client(new BasicAWSCredentials("test-key", "test-secret"), new AmazonS3Config
        {
            ServiceURL = _server.Url,
            ForcePathStyle = true, // the fake endpoint has no per-bucket host names
            AuthenticationRegion = "auto",
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            MaxErrorRetry = 0,
        });
    }

    [Fact]
    public async Task Exists_maps_200_and_404()
    {
        _server.Given(Request.Create().WithPath($"/{Bucket}/present").UsingHead()).RespondWith(Response.Create().WithStatusCode(200));
        _server.Given(Request.Create().WithPath($"/{Bucket}/absent").UsingHead()).RespondWith(Response.Create().WithStatusCode(404));

        (await Store().ExistsAsync("present", Ct)).ShouldBeTrue();
        (await Store().ExistsAsync("absent", Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Get_returns_the_bytes_or_null_when_missing()
    {
        _server.Given(Request.Create().WithPath($"/{Bucket}/state.json").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("{}"));
        _server.Given(Request.Create().WithPath($"/{Bucket}/missing.json").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404).WithHeader("Content-Type", "application/xml")
                .WithBody("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>NoSuchKey</Code><Message>The specified key does not exist.</Message></Error>"));

        var found = await Store().GetAsync("state.json", 1024, Ct);
        var missing = await Store().GetAsync("missing.json", 1024, Ct);

        Encoding.UTF8.GetString(found.ShouldNotBeNull()).ShouldBe("{}");
        missing.ShouldBeNull();
    }

    [Fact]
    public async Task Object_over_the_limit_is_refused()
    {
        _server.Given(Request.Create().WithPath($"/{Bucket}/big.json").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(new string('x', 100)));

        var failure = await Should.ThrowAsync<StorageFailedException>(() => Store().GetAsync("big.json", 10, Ct));

        failure.Key.ShouldBe("big.json");
    }

    [Fact]
    public async Task Denied_request_becomes_a_storage_failure_naming_the_key_and_status()
    {
        _server.Given(Request.Create().WithPath($"/{Bucket}/docs/x").UsingHead()).RespondWith(Response.Create().WithStatusCode(403));

        var failure = await Should.ThrowAsync<StorageFailedException>(() => Store().ExistsAsync("docs/x", Ct));

        failure.Key.ShouldBe("docs/x");
        failure.Message.ShouldContain("403");
        failure.Message.ShouldNotContain("test-secret");
    }

    [Fact]
    public void Put_is_sent_unsigned_and_leaves_the_stream_to_the_caller()
    {
        using var body = new MemoryStream();

        var request = S3ObjectStore.CreatePutRequest(Bucket, "docs/sha256/ab/abc", body, "application/pdf");

        request.BucketName.ShouldBe(Bucket);
        request.Key.ShouldBe("docs/sha256/ab/abc");
        request.ContentType.ShouldBe("application/pdf");
        request.InputStream.ShouldBeSameAs(body);
        request.DisablePayloadSigning.ShouldBe(true);
        request.AutoCloseStream.ShouldBe(false);
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Stop();
        _server.Dispose();
        GC.SuppressFinalize(this);
    }

    private S3ObjectStore Store() => new(_client, Bucket);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
