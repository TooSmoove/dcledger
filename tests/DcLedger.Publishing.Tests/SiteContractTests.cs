using System.Text.Json;
using DcLedger.Publishing.SiteData;
using DcLedger.Testing;
using Shouldly;
using Xunit;

namespace DcLedger.Publishing.Tests;

/// <summary>
/// The .NET half of the events.json contract (ADR-0004): what the exporter writes has the same shape as
/// site/tests/fixtures/events.json, which the site's own tests and content schema are written against. Same keys in
/// the same order, same JSON kinds; a nullable field may be null on either side.
/// </summary>
public sealed class SiteContractTests
{
    private static readonly HashSet<string> Nullable = new(StringComparer.Ordinal) { "case_number", "last_checked_utc" };

    [Fact]
    public void Exported_events_json_has_the_shape_of_the_site_fixture()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(TestFiles.RepositoryPath("site", "tests", "fixtures", "events.json")));
        using var exported = JsonDocument.Parse(TestFiles.Text(new SiteDataExporter().Export(ExportSamples.Model())[0]));

        var problems = new List<string>();
        CompareShape(fixture.RootElement, exported.RootElement, "$", problems);

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void The_site_fixture_uses_the_schema_version_the_exporter_writes()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(TestFiles.RepositoryPath("site", "tests", "fixtures", "events.json")));

        fixture.RootElement.GetProperty("schema_version").GetInt32().ShouldBe(SiteDataExporter.SchemaVersion);
    }

    /// <summary>JSON has one boolean type; System.Text.Json reports two kinds.</summary>
    private static string Kind(JsonElement element) =>
        element.ValueKind is JsonValueKind.True or JsonValueKind.False ? "Boolean" : element.ValueKind.ToString();

    private static void CompareShape(JsonElement expected, JsonElement actual, string path, List<string> problems)
    {
        var name = path[(path.LastIndexOf('.') + 1)..];
        if (Nullable.Contains(name) && (expected.ValueKind == JsonValueKind.Null || actual.ValueKind == JsonValueKind.Null))
        {
            return;
        }

        if (Kind(expected) != Kind(actual))
        {
            problems.Add($"{path}: the site expects {Kind(expected)}, the exporter wrote {Kind(actual)}.");
            return;
        }

        if (expected.ValueKind == JsonValueKind.Object)
        {
            var expectedKeys = expected.EnumerateObject().Select(p => p.Name).ToList();
            var actualKeys = actual.EnumerateObject().Select(p => p.Name).ToList();
            if (!expectedKeys.SequenceEqual(actualKeys, StringComparer.Ordinal))
            {
                problems.Add($"{path}: the site expects keys [{string.Join(", ", expectedKeys)}], the exporter wrote [{string.Join(", ", actualKeys)}].");
                return;
            }

            foreach (var key in expectedKeys)
            {
                CompareShape(expected.GetProperty(key), actual.GetProperty(key), $"{path}.{key}", problems);
            }
        }
        else if (expected.ValueKind == JsonValueKind.Array)
        {
            if (expected.GetArrayLength() == 0 || actual.GetArrayLength() == 0)
            {
                problems.Add($"{path}: both sides need at least one element to compare.");
                return;
            }

            // Every element of either side has the shape of the fixture's first element.
            var template = expected[0];
            foreach (var element in expected.EnumerateArray().Concat(actual.EnumerateArray()))
            {
                CompareShape(template, element, $"{path}[]", problems);
            }
        }
    }
}
