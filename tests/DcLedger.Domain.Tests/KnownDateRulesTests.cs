using System.Text.Json;
using Shouldly;
using Xunit;

namespace DcLedger.Domain.Tests;

/// <summary>ADR-0010's pure rules: which dates an action states, their state at D, the UID and the reminder days.</summary>
public sealed class KnownDateRulesTests
{
    public static TheoryData<string> CaseNames() => [.. Cases().Select(c => c.GetProperty("name").GetString()!)];

    /// <summary>The shared table (tests/fixtures/known-dates/cases.json) that site/tests/ahead.test.mjs reads too.</summary>
    [Theory]
    [MemberData(nameof(CaseNames))]
    public void State_matches_the_shared_cases(string name)
    {
        var c = Cases().Single(item => item.GetProperty("name").GetString() == name);
        var kind = c.GetProperty("kind").GetString() == "expiry" ? KnownDateKind.Expiry : KnownDateKind.Next;
        var date = DateOnly.Parse(c.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var action = Action() with
        {
            Terms = new DecisionTerms
            {
                ExpiresOn = kind == KnownDateKind.Expiry ? date : null,
                Status = Status(c.GetProperty("status").GetString()),
                SuccessorId = c.GetProperty("successor_id").GetString(),
            },
            NextOn = kind == KnownDateKind.Next ? date : null,
        };
        var known = KnownDateRules.Of(action).Single(k => k.Kind == kind);
        var today = DateOnly.Parse(c.GetProperty("today").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

        KnownDateRules.StateAt(known, action, c.GetProperty("something_on_date").GetBoolean(), today).ToToken()
            .ShouldBe(c.GetProperty("state").GetString());
    }

    [Fact]
    public void An_action_states_its_expiry_then_its_next_step()
    {
        var action = Action() with { Terms = new DecisionTerms { ExpiresOn = new DateOnly(2027, 2, 14) }, NextOn = new DateOnly(2026, 12, 3) };

        KnownDateRules.Of(action).Select(k => (k.Kind, k.Date, k.ActionId, k.JurisdictionId)).ShouldBe(
        [
            (KnownDateKind.Expiry, new DateOnly(2027, 2, 14), action.Id, "greensboro"),
            (KnownDateKind.Next, new DateOnly(2026, 12, 3), action.Id, "greensboro"),
        ]);
    }

    [Fact]
    public void An_action_without_dates_states_none()
    {
        KnownDateRules.Of(Action()).ShouldBeEmpty();
    }

    [Fact]
    public void Uid_is_a_stable_hash_of_known_kind_and_action_id()
    {
        // Golden values: sha256("known\nexpiry\ngreensboro-moratorium-2026-08-18") and the "next" twin. Changing them
        // duplicates every subscribed entry.
        KnownDateRules.Uid(KnownDateKind.Expiry, "greensboro-moratorium-2026-08-18")
            .ShouldBe("76589a41d3e713922b098d70fdfcb961e0dba4a1531ccfe97e0fa363bb6dcfd6@dcledger");
        KnownDateRules.Uid(KnownDateKind.Next, "davidson-moratorium-2026-09-28")
            .ShouldBe("5132b899602280feb072b36ea4fff2d64ff7eb7dabb87cae1763fca300ae1555@dcledger");
    }

    [Fact]
    public void A_rescheduled_date_keeps_its_uid()
    {
        var first = KnownDateRules.Of(Action() with { NextOn = new DateOnly(2026, 12, 3) }).Single();
        var moved = KnownDateRules.Of(Action() with { NextOn = new DateOnly(2026, 12, 17) }).Single();

        moved.Uid.ShouldBe(first.Uid);
        moved.Anchor.ShouldBe("greensboro-moratorium-2026-08-18-next");
    }

    [Fact]
    public void Expiry_and_next_of_one_action_have_different_uids()
    {
        KnownDateRules.Uid(KnownDateKind.Expiry, "a").ShouldNotBe(KnownDateRules.Uid(KnownDateKind.Next, "a"));
    }

    [Fact]
    public void Reminder_leads_are_30_then_7_days()
    {
        KnownDateRules.ReminderLeadDays.ShouldBe([30, 7]);
    }

    [Theory]
    [InlineData(30, 2027, 1, 14, false, "the day before the 30-day mark")]
    [InlineData(30, 2027, 1, 15, true, "on the 30-day mark")]
    [InlineData(7, 2027, 2, 6, false, "the day before the 7-day mark")]
    [InlineData(7, 2027, 2, 7, true, "on the 7-day mark")]
    public void A_reminder_is_due_from_its_day(int lead, int year, int month, int day, bool due, string because)
    {
        var expiry = KnownDateRules.Of(Action() with { Terms = new DecisionTerms { ExpiresOn = new DateOnly(2027, 2, 14) } }).Single();
        var published = new DateTimeOffset(2026, 8, 19, 13, 0, 0, TimeSpan.Zero);

        KnownDateRules.ReminderIsDue(expiry, lead, new DateOnly(year, month, day), published).ShouldBe(due, because);
    }

    [Fact]
    public void A_reminder_whose_day_comes_before_the_decision_was_published_is_skipped()
    {
        // A continuance recorded 12 days ahead: the decision's own item announced the date, so only the 7-day reminder.
        var next = KnownDateRules.Of(Action() with { NextOn = new DateOnly(2026, 12, 3) }).Single();
        var published = new DateTimeOffset(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);
        var today = new DateOnly(2026, 11, 26);

        KnownDateRules.ReminderIsDue(next, 30, today, published).ShouldBeFalse();
        KnownDateRules.ReminderIsDue(next, 7, today, published).ShouldBeTrue();
    }

    [Fact]
    public void A_reminder_on_the_day_the_decision_was_published_is_skipped_when_published_after_midnight()
    {
        var next = KnownDateRules.Of(Action() with { NextOn = new DateOnly(2026, 12, 3) }).Single();
        var publishedThatMorning = new DateTimeOffset(2026, 11, 26, 14, 0, 0, TimeSpan.Zero);

        KnownDateRules.ReminderIsDue(next, 7, new DateOnly(2026, 11, 26), publishedThatMorning).ShouldBeFalse();
    }

    [Theory]
    [InlineData(2027, 1, 15, 5)]
    [InlineData(2026, 7, 15, 4)]
    public void Eastern_midnight_carries_the_offset_in_effect(int year, int month, int day, int hoursBehind)
    {
        KnownDateRules.EasternMidnight(new DateOnly(year, month, day))
            .ShouldBe(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.FromHours(-hoursBehind)));
    }

    [Fact]
    public void Eastern_date_of_an_evening_utc_instant_is_the_day_before()
    {
        KnownDateRules.EasternDate(new DateTimeOffset(2026, 11, 22, 3, 0, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 11, 21));
    }

    [Fact]
    public void Kind_and_state_tokens_are_fixed()
    {
        KnownDateKind.Expiry.ToToken().ShouldBe("expiry");
        KnownDateKind.Next.ToToken().ShouldBe("next");
        KnownDateState.Upcoming.ToToken().ShouldBe("upcoming");
        KnownDateState.Ended.ToToken().ShouldBe("ended");
        KnownDateState.Recorded.ToToken().ShouldBe("recorded");
        KnownDateState.PassedUnrecorded.ToToken().ShouldBe("passed_unrecorded");
    }

    private static ActionStatus? Status(string? token) => token switch
    {
        null => null,
        "in_effect" => ActionStatus.InEffect,
        "expired" => ActionStatus.Expired,
        "superseded" => ActionStatus.Superseded,
        "repealed" => ActionStatus.Repealed,
        _ => throw new ArgumentOutOfRangeException(nameof(token), token, "Unknown status in cases.json."),
    };

    private static LocalAction Action() => new()
    {
        Id = "greensboro-moratorium-2026-08-18",
        JurisdictionId = "greensboro",
        ActionType = ActionType.Moratorium,
        Outcome = ActionOutcome.Adopted,
        DecidedOn = new DateOnly(2026, 8, 18),
        Label = EvidenceLabel.ConfirmedOfficial,
        DocumentIds = [Sha256Hash.Compute("official"u8)],
    };

    private static List<JsonElement> Cases()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "tests", "fixtures", "known-dates", "cases.json");
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                return [.. document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone())];
            }
        }

        throw new InvalidOperationException("tests/fixtures/known-dates/cases.json not found above " + AppContext.BaseDirectory);
    }
}
