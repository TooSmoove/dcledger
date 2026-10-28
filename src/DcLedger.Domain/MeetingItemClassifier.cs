using System.Text.RegularExpressions;

namespace DcLedger.Domain;

/// <summary>
/// Deterministic keyword rules that decide whether an agenda item gets a review issue. No AI (SPEC §D).
/// Only the strong rules from spike S15 are used: they found every relevant item, and the broader rules
/// found nothing relevant (docs/spikes/2026-10-27-legistar-items.md).
/// </summary>
public static partial class MeetingItemClassifier
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const int TimeoutMilliseconds = 250;

    private static readonly (string Name, Regex Pattern)[] Rules =
    [
        ("data_center", DataCenter()),
        ("datacenter", Datacenter()),
        ("hyperscale", Hyperscale()),
        ("server_farm", ServerFarm()),
        ("colocation", Colocation()),
    ];

    /// <summary>The rule names, in evaluation order.</summary>
    public static IReadOnlyList<string> RuleNames { get; } = [.. Rules.Select(rule => rule.Name)];

    /// <summary>Classifies untrusted agenda text (title, matter name, file number) by keyword.</summary>
    public static Classification Classify(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Classification([.. Rules.Where(rule => rule.Pattern.IsMatch(text)).Select(rule => rule.Name)]);
    }

    [GeneratedRegex(@"\bdata[\s-]*(cent(er|re)s?|processing)\b", Options, TimeoutMilliseconds)]
    private static partial Regex DataCenter();

    [GeneratedRegex(@"\bdatacent(er|re)s?\b", Options, TimeoutMilliseconds)]
    private static partial Regex Datacenter();

    [GeneratedRegex(@"\bhyper[\s-]?scale\b", Options, TimeoutMilliseconds)]
    private static partial Regex Hyperscale();

    [GeneratedRegex(@"\bserver\s+farms?\b", Options, TimeoutMilliseconds)]
    private static partial Regex ServerFarm();

    [GeneratedRegex(@"\bco-?location\s+(facilit|center|centre)", Options, TimeoutMilliseconds)]
    private static partial Regex Colocation();
}

/// <summary>The rules an agenda item matched; a candidate when at least one did.</summary>
public sealed record Classification(IReadOnlyList<string> MatchedRules)
{
    /// <summary>True when the item should get a review issue.</summary>
    public bool IsCandidate => MatchedRules.Count > 0;
}
