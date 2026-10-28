namespace DcLedger.Infrastructure.Storage;

/// <summary>
/// Where the private document bucket is and how to reach it. The account ID and bucket name are repository variables;
/// the key pair is the <c>r2-docs</c> environment's secret (ADR-0003). A class, not a record, so it never prints the secret.
/// </summary>
public sealed class R2Options
{
    /// <summary>Cloudflare account ID; the endpoint is <c>https://{AccountId}.r2.cloudflarestorage.com</c>.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>The private bucket's name.</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>R2 API token access key ID, scoped to this bucket.</summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>R2 API token secret. Never logged.</summary>
    public string SecretAccessKey { get; set; } = string.Empty;
}
