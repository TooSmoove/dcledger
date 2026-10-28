using System.Security.Cryptography;

namespace DcLedger.Domain;

/// <summary>A SHA-256 digest as 64 lowercase hex characters. Identifies a retrieved document (ADR-0002).</summary>
public readonly record struct Sha256Hash
{
    private Sha256Hash(string value) => Value = value;

    /// <summary>The digest as 64 lowercase hex characters.</summary>
    public string Value { get; }

    /// <summary>Hashes <paramref name="bytes"/>.</summary>
    public static Sha256Hash Compute(ReadOnlySpan<byte> bytes) => new(Convert.ToHexStringLower(SHA256.HashData(bytes)));

    /// <summary>Accepts exactly 64 lowercase hex characters; anything else is rejected rather than normalized.</summary>
    public static bool TryParse(string? text, out Sha256Hash hash)
    {
        if (text is { Length: 64 } && text.All(char.IsAsciiHexDigitLower))
        {
            hash = new Sha256Hash(text);
            return true;
        }

        hash = default;
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
