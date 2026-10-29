using System.Text;
using DcLedger.Application;

namespace DcLedger.Publishing.Tests;

/// <summary>Helpers for reading exported files and finding repository files.</summary>
internal static class TestFiles
{
    /// <summary>The exported file at <paramref name="path"/>; fails when there isn't exactly one.</summary>
    public static ExportFile Single(IReadOnlyList<ExportFile> files, string path) =>
        files.Single(file => string.Equals(file.Path, path, StringComparison.Ordinal));

    /// <summary>The file's bytes decoded as UTF-8 (a BOM, if any, stays visible as U+FEFF).</summary>
    public static string Text(ExportFile file) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(file.Content.Span);

    /// <summary>A path under the repository root (the directory holding DcLedger.slnx).</summary>
    public static string RepositoryPath(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DcLedger.slnx")))
            {
                return Path.Combine([directory.FullName, .. parts]);
            }
        }

        throw new InvalidOperationException("DcLedger.slnx not found above " + AppContext.BaseDirectory);
    }
}
