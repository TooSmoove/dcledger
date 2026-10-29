using System.Text;
using DcLedger.Domain;

namespace DcLedger.Publishing.Ics;

/// <summary>
/// The hand-written America/New_York VTIMEZONE (2007+ rules), pinned byte for byte (SPEC AC3, spike S6). Ical.Net 5
/// emits no VTIMEZONE for a TZID'd event, and its own generator has known gaps, so this block is inserted instead.
/// </summary>
internal static class PinnedTimeZone
{
    /// <summary>The TZID every event's DTSTART uses.</summary>
    public const string Id = "America/New_York";

    /// <summary>SHA-256 of the pinned block, as checked in spike S6.</summary>
    public const string Sha256 = "0c5fc3087bfde95550c1936bbe2d9bd1098a4f93010f7e96bc1830ec60032136";

    private const string ResourceName = "DcLedger.Publishing.america-new_york.vtimezone.ics";
    private const string BeginEvent = "\r\nBEGIN:VEVENT\r\n";
    private const string EndCalendar = "\r\nEND:VCALENDAR";
    private const string BeginTimeZone = "\r\nBEGIN:VTIMEZONE\r\n";

    /// <summary>The block's text, CRLF line endings included.</summary>
    public static string Block { get; } = Load();

    /// <summary>
    /// Inserts the block on its own lines before the first VEVENT, or before END:VCALENDAR when there is none (a
    /// calendar needs at least one component, RFC 5545 §3.6). Searches whole lines, so event text can't move it.
    /// </summary>
    public static string InsertInto(string ics)
    {
        ArgumentNullException.ThrowIfNull(ics);
        if (ics.Contains(BeginTimeZone, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ical.Net emitted a VTIMEZONE of its own; its behavior changed since spike S6.");
        }

        var at = ics.IndexOf(BeginEvent, StringComparison.Ordinal);
        if (at < 0)
        {
            at = ics.IndexOf(EndCalendar, StringComparison.Ordinal);
        }

        if (at < 0)
        {
            throw new InvalidOperationException("The serialized calendar has no END:VCALENDAR line.");
        }

        return ics.Insert(at + 2, Block);
    }

    private static string Load()
    {
        using var stream = typeof(PinnedTimeZone).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();

        var hash = Sha256Hash.Compute(bytes).Value;
        if (!string.Equals(hash, Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The pinned VTIMEZONE hashes to {hash}, not {Sha256}. Were its CRLF line endings normalized? See .gitattributes.");
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
