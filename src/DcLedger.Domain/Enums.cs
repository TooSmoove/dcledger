namespace DcLedger.Domain;

/// <summary>Whether a meeting is expected to happen.</summary>
public enum EventStatus
{
    /// <summary>On the calendar.</summary>
    Scheduled,

    /// <summary>Cancelled; kept so calendar subscribers see the cancellation (SPEC AC4).</summary>
    Cancelled,
}

/// <summary>How well a public item is sourced (SPEC AC6). Applied mechanically, never editorially.</summary>
public enum EvidenceLabel
{
    /// <summary>Known only from news coverage.</summary>
    ReportedNews,

    /// <summary>Backed by a document from an official platform.</summary>
    ConfirmedOfficial,
}

/// <summary>Who published a document.</summary>
public enum SourceType
{
    /// <summary>A government body or its agenda platform.</summary>
    Official,

    /// <summary>A news organization.</summary>
    News,
}
