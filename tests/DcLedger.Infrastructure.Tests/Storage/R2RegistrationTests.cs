using Amazon.Runtime;
using DcLedger.Application;
using DcLedger.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace DcLedger.Infrastructure.Tests.Storage;

public sealed class R2RegistrationTests
{
    private const string Secret = "do-not-print-this-secret";

    [Fact]
    public void Client_uses_the_settings_spike_s2_s3_proved_against_r2()
    {
        var config = StorageServiceCollectionExtensions.CreateR2Config("0123456789abcdef0123456789abcdef");

        config.ServiceURL.ShouldStartWith("https://0123456789abcdef0123456789abcdef.r2.cloudflarestorage.com");
        config.AuthenticationRegion.ShouldBe("auto");
        config.RequestChecksumCalculation.ShouldBe(RequestChecksumCalculation.WHEN_REQUIRED);
        config.ResponseChecksumValidation.ShouldBe(ResponseChecksumValidation.WHEN_REQUIRED);
        config.ForcePathStyle.ShouldBe(false);
    }

    [Fact]
    public void Stores_resolve_without_touching_the_network()
    {
        using var provider = new ServiceCollection().AddDcLedgerR2Storage(ValidOptions()).BuildServiceProvider();

        provider.GetRequiredService<IDocumentStore>().ShouldNotBeNull();
        provider.GetRequiredService<IIngestState>().ShouldNotBeNull();
        provider.GetRequiredService<IRunLog>().ShouldNotBeNull();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("bucket")]
    [InlineData("key")]
    [InlineData("secret")]
    public void Bad_options_are_rejected_without_echoing_the_secret(string broken)
    {
        var options = ValidOptions();
        switch (broken)
        {
            case "account":
                options.AccountId = "evil.example/";
                break;
            case "bucket":
                options.Bucket = "Not_A_Bucket";
                break;
            case "key":
                options.AccessKeyId = " ";
                break;
            default:
                options.SecretAccessKey = string.Empty;
                break;
        }

        var failure = Should.Throw<ArgumentException>(() => new ServiceCollection().AddDcLedgerR2Storage(options));

        failure.Message.ShouldNotContain(Secret);
    }

    private static R2Options ValidOptions() => new()
    {
        AccountId = "0123456789abcdef0123456789abcdef",
        Bucket = "dcledger-docs",
        AccessKeyId = "key-id",
        SecretAccessKey = Secret,
    };
}
