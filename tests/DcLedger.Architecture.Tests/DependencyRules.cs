using System.Xml.Linq;

namespace DcLedger.Architecture.Tests;

/// <summary>
/// The dependency rules from docs/adr/0001-module-boundaries.md, checked against project files.
/// Adding a project or package dependency means changing this table in the same PR, deliberately.
/// </summary>
internal static class DependencyRules
{
    internal static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedProjects =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["DcLedger.Domain"] = Set(),
            ["DcLedger.Application"] = Set("DcLedger.Domain"),
            ["DcLedger.Adapters.Legistar"] = Set("DcLedger.Application", "DcLedger.Domain"),
            ["DcLedger.Infrastructure"] = Set("DcLedger.Application", "DcLedger.Domain"),
            ["DcLedger.Publishing"] = Set("DcLedger.Application", "DcLedger.Domain"),
            ["DcLedger.Cli"] = Set(
                "DcLedger.Application",
                "DcLedger.Domain",
                "DcLedger.Adapters.Legistar",
                "DcLedger.Infrastructure",
                "DcLedger.Publishing"),
        };

    internal static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedPackages =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["DcLedger.Domain"] = Set(),
            ["DcLedger.Application"] = Set(),
            ["DcLedger.Adapters.Legistar"] = Set(),
            ["DcLedger.Infrastructure"] = Set(
                "AWSSDK.S3",
                "YamlDotNet",
                "Microsoft.Extensions.Http",
                "Microsoft.Extensions.Http.Resilience"),
            ["DcLedger.Publishing"] = Set(
                "DuckDB.NET.Data.Full",
                "Ical.Net",
                "System.ServiceModel.Syndication"),
            ["DcLedger.Cli"] = Set(
                "System.CommandLine",
                "Serilog",
                "Serilog.Sinks.Console",
                "Microsoft.Extensions.DependencyInjection"),
        };

    /// <summary>Returns one message per rule broken by <paramref name="projectXml"/>.</summary>
    internal static IReadOnlyList<string> Violations(string projectName, string projectXml)
    {
        if (!AllowedProjects.TryGetValue(projectName, out var allowedProjects)
            || !AllowedPackages.TryGetValue(projectName, out var allowedPackages))
        {
            return [$"{projectName} has no entry in DependencyRules; add one deliberately."];
        }

        var document = XDocument.Parse(projectXml);
        var violations = new List<string>();

        foreach (var reference in Includes(document, "ProjectReference").Select(ProjectNameFromPath))
        {
            if (!allowedProjects.Contains(reference))
            {
                violations.Add($"{projectName} must not reference project {reference}.");
            }
        }

        foreach (var package in Includes(document, "PackageReference"))
        {
            if (!allowedPackages.Contains(package))
            {
                violations.Add($"{projectName} must not reference package {package}.");
            }
        }

        return violations;
    }

    private static IEnumerable<string> Includes(XDocument document, string itemName) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => (string?)element.Attribute("Include"))
            .OfType<string>();

    private static string ProjectNameFromPath(string path) =>
        Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));

    private static HashSet<string> Set(params string[] names) => new(names, StringComparer.Ordinal);
}
