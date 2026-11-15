using DcLedger.Domain;

namespace DcLedger.Publishing;

/// <summary>
/// What readers are told about a document a person saved in a browser (<see cref="Retrieval.Manual"/>, issues #101 and
/// #113). The feed and the calendars use this wording, and the site shows the same sentence
/// (<c>retrievalText</c> in site/src/lib/radar.js). A fetched copy gets no note.
/// </summary>
internal static class RetrievalNote
{
    /// <summary>The sentence, without a final period.</summary>
    public const string Manual = "Saved by a person in a browser (the source does not allow automated downloads)";

    /// <summary>
    /// The text that follows a source's "(retrieved …, SHA-256 …)": ". " plus <see cref="Manual"/> and a period for a
    /// manual copy, nothing for a fetched one.
    /// </summary>
    public static string After(Document document) => document.Retrieval switch
    {
        Retrieval.Fetched => string.Empty,
        Retrieval.Manual => $". {Manual}.",
        _ => throw new ArgumentOutOfRangeException(nameof(document), document.Retrieval, "Unknown retrieval."),
    };
}
