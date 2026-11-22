using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>
/// Everything the exporters publish, resolved and ordered once so every output agrees: jurisdictions by ID, events by
/// start time then ID, actions by decision date then ID, and each item's documents in the order its record lists them.
/// Built only from validated data.
/// </summary>
public sealed record ExportModel
{
    /// <summary>The ingest status's build time: events.json's <c>generated_utc</c> and "now" for the stale flag.</summary>
    public required DateTimeOffset GeneratedUtc { get; init; }

    /// <summary>Every jurisdiction, ordered by ID.</summary>
    public required IReadOnlyList<ExportJurisdiction> Jurisdictions { get; init; }

    /// <summary>Every event, ordered by start time, then ID.</summary>
    public required IReadOnlyList<ExportEvent> Events { get; init; }

    /// <summary>Every local action, ordered by decision date, then ID.</summary>
    public IReadOnlyList<ExportAction> Actions { get; init; } = [];

    /// <summary>Every corrections log entry (issue #117), ordered by correction date, then ID.</summary>
    public IReadOnlyList<Correction> Corrections { get; init; } = [];

    /// <summary>
    /// The first-page picture of each exported document that has one (issue #132), ordered by document ID. The quality
    /// gate has already checked that each may be shown, was reviewed and matches its record's hash.
    /// </summary>
    public IReadOnlyList<ThumbnailFile> Thumbnails { get; init; } = [];

    /// <summary>
    /// Every known date the record states (ADR-0010), with its state at D, the Eastern date of
    /// <see cref="GeneratedUtc"/>; ordered by date, then kind, then action ID.
    /// </summary>
    public IReadOnlyList<ExportKnownDate> KnownDates { get; init; } = [];

    /// <summary>D: the Eastern calendar date of <see cref="GeneratedUtc"/>, against which known dates are placed.</summary>
    public DateOnly Today => KnownDateRules.EasternDate(GeneratedUtc);
}

/// <summary>A known date with its state at D and the action that states it, resolved (ADR-0010).</summary>
public sealed record ExportKnownDate
{
    /// <summary>The date.</summary>
    public required KnownDate Date { get; init; }

    /// <summary>Where it stands at D.</summary>
    public required KnownDateState State { get; init; }

    /// <summary>The action that states it, with its jurisdiction and documents.</summary>
    public required ExportAction Action { get; init; }
}

/// <summary>A jurisdiction with its freshness (SPEC AC10).</summary>
public sealed record ExportJurisdiction
{
    /// <summary>The jurisdiction record.</summary>
    public required Jurisdiction Jurisdiction { get; init; }

    /// <summary>
    /// The last successful ingest check for an automated jurisdiction, or the reviewer's last manual check for a manual
    /// one; null when there is none, and for a jurisdiction that isn't watched (issue #115).
    /// </summary>
    public DateTimeOffset? LastCheckedUtc { get; init; }

    /// <summary>
    /// True after <see cref="StaleAfter"/> without a successful check (<see cref="Freshness"/>), or when a manual
    /// check is dated after the export. Always false for a jurisdiction that isn't watched.
    /// </summary>
    public required bool Stale { get; init; }

    /// <summary>The threshold that applies: 48 hours when automated, 7 days when checked by hand.</summary>
    public TimeSpan StaleAfter { get; init; } = Freshness.StaleAfter;

    /// <summary>The documents behind the jurisdiction's note, in the order its record lists them; empty without a note.</summary>
    public IReadOnlyList<Document> NoteDocuments { get; init; } = [];
}

/// <summary>An event with its jurisdiction and documents resolved.</summary>
public sealed record ExportEvent
{
    /// <summary>The event record.</summary>
    public required LedgerEvent Event { get; init; }

    /// <summary>The event's jurisdiction.</summary>
    public required Jurisdiction Jurisdiction { get; init; }

    /// <summary>The event's documents, in the order its record lists them; never empty (SPEC AC2).</summary>
    public required IReadOnlyList<Document> Documents { get; init; }
}

/// <summary>A local action with its jurisdiction and documents resolved.</summary>
public sealed record ExportAction
{
    /// <summary>The action record.</summary>
    public required LocalAction Action { get; init; }

    /// <summary>The action's jurisdiction.</summary>
    public required Jurisdiction Jurisdiction { get; init; }

    /// <summary>The action's documents, in the order its record lists them; never empty (SPEC AC2).</summary>
    public required IReadOnlyList<Document> Documents { get; init; }
}
