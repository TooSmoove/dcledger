using System.Text.RegularExpressions;

namespace DcLedger.Application;

/// <summary>
/// One exported file: its path relative to the output directory and its bytes. Paths are lowercase, '/'-separated
/// and can't escape the output directory, so a jurisdiction ID can't steer a write, and two names never collide on a
/// case-insensitive file system.
/// </summary>
public sealed partial class ExportFile
{
    /// <summary>Creates the file. Throws <see cref="ArgumentException"/> for an unsafe path.</summary>
    public ExportFile(string path, ReadOnlyMemory<byte> content)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!IsSafePath(path))
        {
            throw new ArgumentException($"'{path}' isn't a safe relative path: use lowercase segments of letters, digits, '.', '_' or '-', separated by '/'.", nameof(path));
        }

        Path = path;
        Content = content;
    }

    /// <summary>Path relative to the output directory, for example <c>public/radar/guilford.ics</c>.</summary>
    public string Path { get; }

    /// <summary>The file's bytes.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>True for paths made of safe lowercase segments, none of them "." or "..".</summary>
    public static bool IsSafePath(string path) =>
        path is not null && path.Split('/').All(segment => Segment().IsMatch(segment) && segment is not ("." or ".."));

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Segment();
}
