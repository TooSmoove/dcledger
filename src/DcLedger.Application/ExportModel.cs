using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>
/// Everything the exporters publish, resolved and ordered once so every output agrees: jurisdictions by ID, events by
/// start time then ID, each event's documents in the order its record lists them. Built only from validated data.
/// </summary>
public sealed record ExportModel
{
    /// <summary>The ingest status's build time: events.json's <c>generated_utc</c> and "now" for the stale flag.</summary>
    public required DateTimeOffset GeneratedUtc { get; init; }

    /// <summary>Every jurisdiction, ordered by ID.</summary>
    public required IReadOnlyList<ExportJurisdiction> Jurisdictions { get; init; }

    /// <summary>Every event, ordered by start time, then ID.</summary>
    public required IReadOnlyList<ExportEvent> Events { get; init; }
}

/// <summary>A jurisdiction with its freshness (SPEC AC10).</summary>
public sealed record ExportJurisdiction
{
    /// <summary>The jurisdiction record.</summary>
    public required Jurisdiction Jurisdiction { get; init; }

    /// <summary>
    /// The last successful ingest check for an automated jurisdiction, or the reviewer's last manual check for a manual
    /// one; null when there is none.
    /// </summary>
    public DateTimeOffset? LastCheckedUtc { get; init; }

    /// <summary>True after 48 hours without a successful check (<see cref="Freshness"/>).</summary>
    public required bool Stale { get; init; }
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
