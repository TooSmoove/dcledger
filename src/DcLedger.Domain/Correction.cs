namespace DcLedger.Domain;

/// <summary>
/// One entry in the public corrections log (issue #117): what was wrong in a published record and when it was fixed.
/// One YAML file per entry under <c>data/corrections/</c>, so a published entry can't be removed without failing the
/// quality gate, like any other record (ADR-0002). The log is a page on the site, not a record type in the public
/// downloads (SPEC §D).
/// </summary>
public sealed record Correction
{
    /// <summary>
    /// The date the log opened, shown on the page. A log that starts on launch day is complete; no entry is dated
    /// before it.
    /// </summary>
    public static readonly DateOnly LogOpenedOn = new(2026, 11, 22);

    /// <summary>The most characters a summary may hold: one plain sentence or two, like a decision term.</summary>
    public const int MaxSummaryLength = 500;

    /// <summary>Immutable slug, for example <c>2026-11-24-guilford-vote-count</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The Eastern date the problem was reported or found.</summary>
    public required DateOnly ReportedOn { get; init; }

    /// <summary>The Eastern date the corrected record was published.</summary>
    public required DateOnly CorrectedOn { get; init; }

    /// <summary>
    /// The jurisdiction, event or action IDs the correction changed; empty when it changed only page wording.
    /// </summary>
    public IReadOnlyList<string> RecordIds { get; init; } = [];

    /// <summary>What was wrong and what the record says now, in neutral words (SPEC §E), on one line.</summary>
    public required string Summary { get; init; }

    /// <summary>
    /// The number of the site form report it came from (shown as R-&lt;n&gt;), when there is one. Reports themselves are
    /// private (docs/reports.md); only the number is published.
    /// </summary>
    public int? Report { get; init; }
}
