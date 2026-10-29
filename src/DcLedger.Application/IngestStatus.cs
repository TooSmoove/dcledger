namespace DcLedger.Application;

/// <summary>
/// When each automated jurisdiction was last checked by a successful ingest run, as of <see cref="GeneratedUtc"/>.
/// Publish builds it from ingest run history and passes it to <c>dcl export --status</c>, so export never calls GitHub
/// (ADR-0004). The file format is read in Infrastructure.
/// </summary>
public sealed record IngestStatus
{
    /// <summary>When the status was built. Export uses it as "now" for the stale flag and as events.json's generated time.</summary>
    public required DateTimeOffset GeneratedUtc { get; init; }

    /// <summary>Last successful check per jurisdiction ID; null when the history has no successful run.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset?> LastCheckedUtc { get; init; } = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
}
