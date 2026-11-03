using DcLedger.Infrastructure.Storage;

namespace DcLedger.Cli;

/// <summary>
/// The private bucket's settings as the <c>r2-docs</c> environment provides them (ADR-0003): repository variables for
/// the account and bucket, secrets for the key. Shared by every command that writes to the bucket.
/// </summary>
internal static class R2Environment
{
    /// <summary>The variables the bucket needs. Values are never printed.</summary>
    internal static readonly IReadOnlyList<string> Variables =
    [
        "R2_ACCOUNT_ID",
        "R2_PRIVATE_BUCKET",
        "R2_PRIVATE_ACCESS_KEY_ID",
        "R2_PRIVATE_SECRET_ACCESS_KEY",
    ];

    /// <summary>Throws <see cref="ArgumentException"/> naming every blank variable in <paramref name="names"/>, never their values.</summary>
    internal static void RequireAll(IEnumerable<string> names, Func<string, string?> environment)
    {
        var missing = names.Where(name => string.IsNullOrWhiteSpace(environment(name))).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException($"Missing environment variables: {string.Join(", ", missing)}.", nameof(environment));
        }
    }

    /// <summary>The bucket options, trimmed. Call <see cref="RequireAll"/> first.</summary>
    internal static R2Options Options(Func<string, string?> environment) => new()
    {
        AccountId = environment("R2_ACCOUNT_ID")!.Trim(),
        Bucket = environment("R2_PRIVATE_BUCKET")!.Trim(),
        AccessKeyId = environment("R2_PRIVATE_ACCESS_KEY_ID")!.Trim(),
        SecretAccessKey = environment("R2_PRIVATE_SECRET_ACCESS_KEY")!.Trim(),
    };
}
