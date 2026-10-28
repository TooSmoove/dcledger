using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using DcLedger.Application;
using Microsoft.Extensions.DependencyInjection;

namespace DcLedger.Infrastructure.Storage;

/// <summary>Registers the private R2 bucket behind <see cref="IDocumentStore"/>, <see cref="IIngestState"/> and <see cref="IRunLog"/>.</summary>
public static partial class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Adds the stores. Throws <see cref="ArgumentException"/> for incomplete or malformed options (exit code 2); the
    /// message never contains the secret.
    /// </summary>
    public static IServiceCollection AddDcLedgerR2Storage(this IServiceCollection services, R2Options options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var accountId = options.AccountId;
        var bucket = options.Bucket;
        var credentials = new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);

        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(credentials, CreateR2Config(accountId)));
        services.AddSingleton<IObjectStore>(provider => new S3ObjectStore(provider.GetRequiredService<IAmazonS3>(), bucket));
        services.AddSingleton<IDocumentStore, BucketDocumentStore>();
        services.AddSingleton<IIngestState, BucketIngestState>();
        services.AddSingleton<IRunLog, BucketRunLog>();
        return services;
    }

    /// <summary>The client settings spike S2/S3 proved against R2 with AWSSDK.S3 4.0.104.6.</summary>
    internal static AmazonS3Config CreateR2Config(string accountId) => new()
    {
        ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
        AuthenticationRegion = "auto",
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    };

    private static void Validate(R2Options options)
    {
        if (!AccountIdPattern().IsMatch(options.AccountId))
        {
            throw new ArgumentException("R2 account ID must be letters and digits only.", nameof(options));
        }

        if (!BucketPattern().IsMatch(options.Bucket))
        {
            throw new ArgumentException("R2 bucket name must be 3-63 lowercase letters, digits or hyphens.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.AccessKeyId) || string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            throw new ArgumentException("R2 access key ID and secret are both required.", nameof(options));
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex AccountIdPattern();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]\z", RegexOptions.CultureInvariant)]
    private static partial Regex BucketPattern();
}
