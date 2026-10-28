using System.Text;

namespace DcLedger.Domain;

/// <summary>
/// The iCalendar UID of a meeting: a hash of jurisdiction, platform and the platform's meeting ID (SPEC AC3).
/// It never changes when a meeting is rescheduled or cancelled, so calendar apps update the event in place.
/// </summary>
public static class EventUid
{
    private const string Suffix = "@dcledger";

    /// <summary>Returns <c>&lt;64 hex&gt;@dcledger</c>. Platform tokens are fixed strings so renaming the enum can't change UIDs.</summary>
    public static string Create(string jurisdictionId, Platform platform, string platformMeetingId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jurisdictionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(platformMeetingId);

        var token = platform switch
        {
            Platform.Legistar => "legistar",
            Platform.Manual => "manual",
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unknown platform."),
        };

        var key = Encoding.UTF8.GetBytes($"{jurisdictionId}\n{token}\n{platformMeetingId}");
        return Sha256Hash.Compute(key).Value + Suffix;
    }
}
