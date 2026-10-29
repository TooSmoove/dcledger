using DcLedger.Application;

namespace DcLedger.Infrastructure.Export;

/// <summary>
/// Writes exported files under one output directory. Each file is written to a temporary name and then moved into
/// place, so a reader never sees half a file. Files of the same name are replaced; nothing is ever deleted.
/// </summary>
public sealed class DirectoryExportSink : IExportSink
{
    private readonly string _root;
    private readonly string _rootPrefix;

    /// <summary>Writes under <paramref name="outputDirectory"/>, creating it when needed.</summary>
    public DirectoryExportSink(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        _rootPrefix = Path.EndsInDirectorySeparator(_root) ? _root : _root + Path.DirectorySeparatorChar;
    }

    /// <inheritdoc />
    public void Write(ExportFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var target = Path.GetFullPath(Path.Join(_root, file.Path));
        if (!target.StartsWith(_rootPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{file.Path}' resolves outside {_root}.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, file.Content.Span);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
