namespace DcLedger.Application;

/// <summary>
/// The media type of a file a person saved (issue #101), where no server reported one. The bytes decide for PDFs, which is
/// almost every agenda and minutes file; otherwise a few known extensions; otherwise <c>application/octet-stream</c>.
/// </summary>
public static class MediaTypeGuess
{
    private const string Unknown = "application/octet-stream";

    /// <summary>Guesses from <paramref name="content"/>'s first bytes, then <paramref name="fileName"/>'s extension.</summary>
    public static string Of(ReadOnlySpan<byte> content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        if (content.StartsWith("%PDF-"u8))
        {
            return "application/pdf";
        }

        var extension = Path.GetExtension(fileName);
        if (Is(extension, ".html") || Is(extension, ".htm"))
        {
            return "text/html";
        }

        return Is(extension, ".txt") ? "text/plain" : Unknown;
    }

    private static bool Is(string extension, string known) => string.Equals(extension, known, StringComparison.OrdinalIgnoreCase);
}
