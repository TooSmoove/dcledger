namespace DcLedger.Domain;

/// <summary>A local government whose meetings are tracked. One YAML file per jurisdiction (ADR-0002).</summary>
public sealed record Jurisdiction
{
    /// <summary>Immutable slug, for example <c>guilford</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Where meetings come from.</summary>
    public required Platform Platform { get; init; }

    /// <summary>Legistar client name in <c>webapi.legistar.com/v1/{client}</c>; null for manual jurisdictions.</summary>
    public string? LegistarClient { get; init; }

    /// <summary>Legistar body names to ingest, exactly as the API spells them.</summary>
    public IReadOnlyList<string> Bodies { get; init; } = [];

    /// <summary>When a reviewer last checked a manual jurisdiction's official site (SPEC AC10).</summary>
    public DateTimeOffset? LastManualCheckUtc { get; init; }
}

/// <summary>A retrieved source document. Only metadata is public; the bytes stay in the private bucket (ADR-0003).</summary>
public sealed record Document
{
    /// <summary>SHA-256 of the retrieved bytes; also the document's ID.</summary>
    public required Sha256Hash Id { get; init; }

    /// <summary>Where the document was retrieved from.</summary>
    public required Uri Url { get; init; }

    /// <summary>When it was retrieved, in UTC.</summary>
    public required DateTimeOffset RetrievedAtUtc { get; init; }

    /// <summary>Media type reported by the source, for example <c>application/pdf</c>.</summary>
    public required string MediaType { get; init; }

    /// <summary>Size of the retrieved bytes.</summary>
    public required long Bytes { get; init; }

    /// <summary>Who published it.</summary>
    public required SourceType SourceType { get; init; }
}

/// <summary>A reviewed meeting or hearing on the radar. Named LedgerEvent to avoid the C# <c>event</c> keyword.</summary>
public sealed record LedgerEvent
{
    /// <summary>Immutable public slug used in URLs.</summary>
    public required string Id { get; init; }

    /// <summary>The jurisdiction's <see cref="Jurisdiction.Id"/>.</summary>
    public required string JurisdictionId { get; init; }

    /// <summary>Where the meeting came from.</summary>
    public required Platform Platform { get; init; }

    /// <summary>The platform's own meeting ID (Legistar <c>EventId</c>), or a reviewer-chosen ID for manual entries.</summary>
    public required string PlatformMeetingId { get; init; }

    /// <summary>The meeting body, for example "Board of Commissioners".</summary>
    public required string Body { get; init; }

    /// <summary>Reviewer-written neutral title. Never the source's original title.</summary>
    public required string Title { get; init; }

    /// <summary>Case or file number, when there is one.</summary>
    public string? CaseNumber { get; init; }

    /// <summary>Start time with its Eastern offset.</summary>
    public required DateTimeOffset StartsAt { get; init; }

    /// <summary>Scheduled or cancelled.</summary>
    public required EventStatus Status { get; init; }

    /// <summary>iCalendar SEQUENCE; incremented on every published change.</summary>
    public required int Sequence { get; init; }

    /// <summary>How the item is sourced.</summary>
    public required EvidenceLabel Label { get; init; }

    /// <summary>Documents that back this item.</summary>
    public required IReadOnlyList<Sha256Hash> DocumentIds { get; init; }

    /// <summary>When ingest first saw the published agenda (SPEC AC9); null for manual entries.</summary>
    public DateTimeOffset? FirstSeenUtc { get; init; }

    /// <summary>Where the proposed project is, when the reviewer recorded it; its marker on the map (issue #22).</summary>
    public Location? Location { get; init; }

    /// <summary>The stable iCalendar UID.</summary>
    public string Uid => EventUid.Create(JurisdictionId, Platform, PlatformMeetingId);
}

/// <summary>A decision taken by a local government, such as adopting a moratorium.</summary>
public sealed record LocalAction
{
    /// <summary>Immutable public slug.</summary>
    public required string Id { get; init; }

    /// <summary>The jurisdiction's <see cref="Jurisdiction.Id"/>.</summary>
    public required string JurisdictionId { get; init; }

    /// <summary>What kind of action, for example <c>moratorium</c>.</summary>
    public required string ActionType { get; init; }

    /// <summary>The outcome as recorded, for example <c>adopted</c>.</summary>
    public required string Outcome { get; init; }

    /// <summary>The date of the decision.</summary>
    public required DateOnly DecidedOn { get; init; }

    /// <summary>The recorded vote, when available, for example "7-2".</summary>
    public string? Votes { get; init; }

    /// <summary>How the action is sourced.</summary>
    public required EvidenceLabel Label { get; init; }

    /// <summary>Documents that back this action.</summary>
    public required IReadOnlyList<Sha256Hash> DocumentIds { get; init; }
}
