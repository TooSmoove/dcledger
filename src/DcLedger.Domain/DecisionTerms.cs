namespace DcLedger.Domain;

/// <summary>
/// The terms of an adopted decision, such as a moratorium (issue #115): what it covers, above what threshold, until
/// when, what ends it early and what is exempt. G.S. 160D-107 requires a moratorium ordinance to state most of these,
/// so the text values are copied from an archived document as written, never summarized (SPEC §E, neutrality). Every
/// value is optional, and each is a flat key of the action's YAML record and of the exports.
/// </summary>
public sealed record DecisionTerms
{
    /// <summary>The most characters a text term may hold; longer text belongs in the cited document.</summary>
    public const int MaxTextLength = 500;

    /// <summary>No terms recorded.</summary>
    public static DecisionTerms None { get; } = new();

    /// <summary>The ordinance or resolution number, as the body numbers it.</summary>
    public string? InstrumentNumber { get; init; }

    /// <summary>What triggers it, as written, for example "electrical power demand exceeding 10 megawatts".</summary>
    public string? Threshold { get; init; }

    /// <summary>What approvals or area it covers, as written.</summary>
    public string? Scope { get; init; }

    /// <summary>The start date.</summary>
    public DateOnly? EffectiveOn { get; init; }

    /// <summary>The outside expiry date.</summary>
    public DateOnly? ExpiresOn { get; init; }

    /// <summary>What ends it sooner, as written.</summary>
    public string? EarlyTermination { get; init; }

    /// <summary>What is exempt, as written.</summary>
    public string? Exemptions { get; init; }

    /// <summary>The <see cref="LocalAction.Id"/> of the action that replaced or extended it.</summary>
    public string? SuccessorId { get; init; }

    /// <summary>Where it stands, when a reviewer recorded that.</summary>
    public ActionStatus? Status { get; init; }

    /// <summary>True when nothing is recorded.</summary>
    public bool IsEmpty => this == None;
}
