using System.Text.Json;
using DcLedger.Domain;
using Shouldly;
using Xunit;

namespace DcLedger.Publishing.Tests;

/// <summary>
/// The fixed known-date words (ADR-0010) match tests/fixtures/known-dates/cases.json, which site/tests/ahead.test.mjs
/// checks site/src/lib/ahead.js against, so the calendars, the feed and /ahead/ say the same thing.
/// </summary>
public sealed class KnownDateWordsTests
{
    [Fact]
    public void Names_match_the_shared_fixture()
    {
        foreach (var entry in Fixture().GetProperty("names").EnumerateArray())
        {
            var kind = entry.GetProperty("kind").GetString() == "expiry" ? KnownDateKind.Expiry : KnownDateKind.Next;
            var type = Enum.GetValues<ActionType>().Single(t => t.ToToken() == entry.GetProperty("action_type").GetString());

            RecordText.KnownDateName(kind, type, entry.GetProperty("jurisdiction").GetString()!).ShouldBe(entry.GetProperty("name").GetString());
        }
    }

    [Fact]
    public void Flag_and_coverage_lines_match_the_shared_fixture()
    {
        RecordText.PassedUnrecorded.ShouldBe(Fixture().GetProperty("flag").GetString());
        RecordText.KnownDateCoverage.ShouldBe(Fixture().GetProperty("coverage").GetString());
    }

    private static JsonElement Fixture()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(TestFiles.RepositoryPath("tests", "fixtures", "known-dates", "cases.json")));
        return document.RootElement.Clone();
    }
}
