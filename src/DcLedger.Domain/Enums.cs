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

/// <summary>How a document's bytes were obtained (issue #101).</summary>
public enum Retrieval
{
    /// <summary>The tool downloaded them from the document's URL.</summary>
    Fetched,

    /// <summary>
    /// A person saved them from the URL in a browser and handed the file to <c>dcl archive-document --file</c>, because the
    /// source only serves browsers (Greensboro's eSCRIBE challenges every program).
    /// </summary>
    Manual,
}

/// <summary>What kind of decision a local action is. Extend by adding members; the YAML tokens are fixed in <see cref="EnumTokens"/>.</summary>
public enum ActionType
{
    /// <summary>A temporary pause on accepting or approving applications.</summary>
    Moratorium,

    /// <summary>A change to a parcel's zoning district.</summary>
    Rezoning,

    /// <summary>A change to the text of a development or zoning ordinance.</summary>
    TextAmendment,

    /// <summary>A resolution of the governing body.</summary>
    Resolution,
}

/// <summary>What the body did with an item, as recorded on its official platform. Applied mechanically, never editorially.</summary>
public enum ActionOutcome
{
    /// <summary>Approved as presented.</summary>
    Adopted,

    /// <summary>Approved after an amendment.</summary>
    AdoptedAmended,

    /// <summary>A motion to approve failed, or the item was denied.</summary>
    Failed,

    /// <summary>Moved to a later meeting.</summary>
    Continued,

    /// <summary>Set aside without a date.</summary>
    Tabled,
}
