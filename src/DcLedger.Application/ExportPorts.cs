namespace DcLedger.Application;

/// <summary>
/// Turns the export model into files. Implemented in Publishing (ADR-0001). Must be deterministic: the same model
/// gives the same bytes on every run and operating system (WS-AC2).
/// </summary>
public interface IExporter
{
    /// <summary>Returns the files for <paramref name="model"/>; never writes anything itself.</summary>
    IReadOnlyList<ExportFile> Export(ExportModel model);
}

/// <summary>Where exported files go. Implemented in Infrastructure (ADR-0001).</summary>
public interface IExportSink
{
    /// <summary>Writes <paramref name="file"/>, replacing a file of the same name.</summary>
    void Write(ExportFile file);
}
