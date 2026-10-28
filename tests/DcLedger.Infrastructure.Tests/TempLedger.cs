namespace DcLedger.Infrastructure.Tests;

/// <summary>A throwaway data directory for reader tests.</summary>
internal sealed class TempLedger : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("dcl-ledger-").FullName;

    public TempLedger Write(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return this;
    }

    public void Dispose()
    {
        Directory.Delete(Path, recursive: true);
        GC.SuppressFinalize(this);
    }
}
