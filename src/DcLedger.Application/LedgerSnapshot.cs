using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>A record together with the file it came from (relative to the data directory, '/' separators).</summary>
public sealed record LoadedRecord<T>(string File, T Record);

/// <summary>
/// One picture file under <c>data/thumbnails/</c> (issue #132): <paramref name="File"/> relative to the data directory,
/// <paramref name="DocumentId"/> from its name, and its bytes with their SHA-256.
/// </summary>
public sealed record ThumbnailFile(string File, Sha256Hash DocumentId, Sha256Hash ContentHash, ReadOnlyMemory<byte> Content);

/// <summary>Everything read from the data directory: records that parsed, problems found while reading, and every record file seen.</summary>
public sealed record LedgerSnapshot
{
    /// <summary>Jurisdictions that parsed without errors.</summary>
    public IReadOnlyList<LoadedRecord<Jurisdiction>> Jurisdictions { get; init; } = [];

    /// <summary>Documents that parsed without errors.</summary>
    public IReadOnlyList<LoadedRecord<Document>> Documents { get; init; } = [];

    /// <summary>Events that parsed without errors.</summary>
    public IReadOnlyList<LoadedRecord<LedgerEvent>> Events { get; init; } = [];

    /// <summary>Local actions that parsed without errors.</summary>
    public IReadOnlyList<LoadedRecord<LocalAction>> Actions { get; init; } = [];

    /// <summary>Corrections log entries that parsed without errors (issue #117).</summary>
    public IReadOnlyList<LoadedRecord<Correction>> Corrections { get; init; } = [];

    /// <summary>Picture files under <c>data/thumbnails/</c> whose names are document IDs (issue #132).</summary>
    public IReadOnlyList<ThumbnailFile> Thumbnails { get; init; } = [];

    /// <summary>Problems found while reading (shape, format, unknown or forbidden keys).</summary>
    public IReadOnlyList<ValidationError> LoadErrors { get; init; } = [];

    /// <summary>Every record file present, including ones that failed to parse.</summary>
    public IReadOnlyList<string> RecordFiles { get; init; } = [];
}

/// <summary>Reads the ledger data directory. Implemented in Infrastructure (ADR-0001).</summary>
public interface ILedgerReader
{
    /// <summary>Reads every record under <paramref name="dataDirectory"/>. Never throws for bad data; problems go in <see cref="LedgerSnapshot.LoadErrors"/>.</summary>
    LedgerSnapshot Load(string dataDirectory);
}
