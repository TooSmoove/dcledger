namespace DcLedger.Application;

/// <summary>The outcome of <see cref="ExportLedger"/>.</summary>
public sealed record ExportReport
{
    /// <summary>The quality gate's result. When it failed, nothing was written.</summary>
    public required ValidationReport Validation { get; init; }

    /// <summary>The files written, relative to the output directory, in path order.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>Problems that didn't stop the export.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>How many jurisdictions were exported.</summary>
    public int Jurisdictions { get; init; }

    /// <summary>How many events were exported.</summary>
    public int Events { get; init; }

    /// <summary>True when the ledger passed the quality gate and the files were written.</summary>
    public bool Exported => Validation.IsValid;
}

/// <summary>
/// Use case behind <c>dcl export</c> (ADR-0004): read the data directory, refuse to publish anything the quality gate
/// rejects (SPEC AC8), build the export model, run every exporter, then write their files. Every file is produced
/// before the first one is written, so a failing exporter leaves the output directory untouched.
/// </summary>
public sealed class ExportLedger
{
    private readonly ILedgerReader _reader;
    private readonly IReadOnlyList<IExporter> _exporters;
    private readonly IExportSink _sink;

    /// <summary>Creates the use case.</summary>
    public ExportLedger(ILedgerReader reader, IReadOnlyList<IExporter> exporters, IExportSink sink)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _exporters = exporters ?? throw new ArgumentNullException(nameof(exporters));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <summary>Exports <paramref name="dataDirectory"/> with freshness from <paramref name="status"/>.</summary>
    public ExportReport Run(string dataDirectory, IngestStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        var snapshot = _reader.Load(dataDirectory);
        var validation = ValidateLedger.Check(snapshot, []);
        if (!validation.IsValid)
        {
            return new ExportReport { Validation = validation };
        }

        var (model, warnings) = ExportModelBuilder.Build(snapshot, status);
        var files = _exporters
            .SelectMany(exporter => exporter.Export(model))
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToList();
        var duplicate = files.GroupBy(file => file.Path, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Two exporters produced '{duplicate.Key}'.");
        }

        foreach (var file in files)
        {
            _sink.Write(file);
        }

        return new ExportReport
        {
            Validation = validation,
            Files = [.. files.Select(file => file.Path)],
            Warnings = warnings,
            Jurisdictions = model.Jurisdictions.Count,
            Events = model.Events.Count,
        };
    }
}
