using System.Globalization;
using System.Text.RegularExpressions;
using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Infrastructure.Storage;

/// <summary>The private bucket's key layout (issue #16). Changing a layout orphans everything stored under the old one.</summary>
internal static partial class StorageKeys
{
    /// <summary><c>docs/sha256/{first two hex}/{hash}</c>.</summary>
    public static string Document(Sha256Hash sha256)
    {
        var hex = sha256.Value ?? throw new ArgumentException("The hash is empty.", nameof(sha256));
        return $"docs/sha256/{hex[..2]}/{hex}";
    }

    /// <summary><c>state/{platform}/{client}/{meetingId}.json</c>.</summary>
    public static string MeetingState(MeetingKey meeting)
    {
        ArgumentNullException.ThrowIfNull(meeting);
        return $"state/{meeting.Platform.ToToken()}/{Segment(meeting.Client, nameof(meeting))}/{Segment(meeting.MeetingId, nameof(meeting))}.json";
    }

    /// <summary><c>runs/{yyyy}/{MM}/{runId}.json</c>, by the UTC start date.</summary>
    public static string Run(DateTimeOffset startedUtc, string runId)
    {
        var utc = startedUtc.UtcDateTime;
        return string.Create(CultureInfo.InvariantCulture, $"runs/{utc:yyyy}/{utc:MM}/{Segment(runId, nameof(runId))}.json");
    }

    /// <summary>Segments come from configuration and from platform data (untrusted), so only a plain token is accepted.</summary>
    private static string Segment(string value, string parameterName) =>
        value is not null && SafeSegment().IsMatch(value)
            ? value
            : throw new ArgumentException($"'{value}' can't be used in a storage key.", parameterName);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSegment();
}
