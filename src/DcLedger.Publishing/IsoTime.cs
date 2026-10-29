using System.Globalization;
using DcLedger.Domain;

namespace DcLedger.Publishing;

/// <summary>The two timestamp spellings every export uses: UTC with Z, and Eastern with its offset (SPEC AC2).</summary>
internal static class IsoTime
{
    /// <summary>For example <c>2026-11-05T22:30:00Z</c>.</summary>
    public static string Utc(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>For example <c>2026-11-05T17:30:00-05:00</c>: Eastern, with the offset in effect at that instant.</summary>
    public static string Eastern(DateTimeOffset instant) =>
        EasternTime.ToEastern(instant).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
