using DcLedger.Domain;
using Shouldly;
using Xunit;
using static VerifyXunit.Verifier;

namespace DcLedger.Application.Tests;

public sealed class ReviewIssueRendererTests
{
    [Fact]
    public Task Issue_body_golden() => Verify(ReviewIssueRenderer.Render(ReviewCandidates.Hearing()).Body);

    [Fact]
    public Task Issue_body_with_every_warning_golden() => Verify(ReviewIssueRenderer.Render(ReviewCandidates.Troubled()).Body);

    [Fact]
    public Task Agenda_changed_comment_golden() =>
        Verify(ReviewIssueRenderer.RenderAgendaChanged(ReviewCandidates.Hearing(), Sha256Hash.Compute("old agenda"u8)));

    [Fact]
    public Task Meeting_changed_comment_golden() => Verify(ReviewIssueRenderer.RenderMeetingChanged(MovedAndPossiblyCancelled()));

    [Fact]
    public void Meeting_no_longer_listed_shows_the_last_known_time_and_the_cancellation_steps()
    {
        var notice = MovedAndPossiblyCancelled() with { Now = null, Changes = [MeetingChange.NoLongerListed] };

        var comment = ReviewIssueRenderer.RenderMeetingChanged(notice);

        comment.ShouldStartWith("<!-- dc-ledger:meeting-changed legistar/guilford/1599 no-longer-listed 2026-10-28T12:47:03Z -->\n**Possible cancellation**");
        comment.ShouldContain("| Scheduled | Thursday, October 15, 2026 at 5:30 PM |");
        comment.ShouldNotContain("| Was |");
        comment.ShouldContain("**If it's cancelled:**");
        comment.ShouldNotContain("**If it moved:**");
        comment.ShouldNotContain("**If it's back on:**");
    }

    [Fact]
    public void Relisted_meeting_offers_to_undo_a_cancellation()
    {
        var notice = MovedAndPossiblyCancelled() with { Changes = [MeetingChange.ListedAgain] };

        var comment = ReviewIssueRenderer.RenderMeetingChanged(notice);

        comment.ShouldContain("**Meeting changed**");
        comment.ShouldContain("**If it's back on:**");
        comment.ShouldNotContain("**If it's cancelled:**");
    }

    [Fact]
    public void Meeting_changed_comment_escapes_configured_names_and_works_without_a_page()
    {
        var notice = MovedAndPossiblyCancelled() with { JurisdictionName = "Guilford <County> @everyone", MeetingPageUri = null };

        var comment = ReviewIssueRenderer.RenderMeetingChanged(notice);

        comment.ShouldContain("Guilford &lt;County&gt; @&#8203;everyone");
        comment.ShouldContain("| Meeting | Legistar meeting 1599 |");
    }

    private static MeetingChangeNotice MovedAndPossiblyCancelled() => new()
    {
        Meeting = new MeetingKey(Platform.Legistar, "guilford", "1599"),
        JurisdictionName = "Guilford County",
        Body = "Board of Commissioners",
        MeetingPageUri = new Uri("https://guilford.legistar.com/MeetingDetail.aspx?LEGID=1599&GID=590"),
        Before = new MeetingSighting { Date = new DateOnly(2026, 10, 15), Time = new TimeOnly(17, 30), AgendaPublished = true },
        Now = new MeetingSighting { Date = new DateOnly(2026, 10, 22), Time = new TimeOnly(18, 0), PossiblyCancelled = true, AgendaPublished = true },
        Changes = [MeetingChange.Rescheduled, MeetingChange.CancellationMentioned],
        CheckedUtc = ReviewCandidates.Retrieved,
    };

    [Fact]
    public void Title_is_neutral_and_carries_the_filtered_case_number()
    {
        ReviewIssueRenderer.Render(ReviewCandidates.Hearing()).Title
            .ShouldBe("Review: Guilford County, Board of Commissioners, 2026-10-15 (case 2026-439)");
        ReviewIssueRenderer.Render(ReviewCandidates.Troubled()).Title
            .ShouldBe("Review: Guilford County, Board of Commissioners, 2026-10-15 (case Z-2601team, 2026-440)");
    }

    [Fact]
    public void Source_titles_never_reach_the_issue()
    {
        var source = new SourceItem
        {
            ItemId = "38603",
            AgendaNumber = "A.",
            Title = "Xyzzy title naming Jane Q. Resident",
            MatterName = "Plugh matter name",
            CaseNumber = "2026-439",
            MatterType = "Public Hearing",
        };
        var item = ReviewItem.FromMatch(source, MeetingItemClassifier.Classify("data center"));
        var candidate = ReviewCandidates.Hearing() with { Items = [item] };

        var issue = ReviewIssueRenderer.Render(candidate);

        var everything = issue.Title + issue.Body;
        everything.ShouldNotContain("Xyzzy");
        everything.ShouldNotContain("Plugh");
        everything.ShouldNotContain("Resident");
    }

    [Fact]
    public void Hostile_source_text_cannot_mention_inject_markup_or_break_the_table()
    {
        var body = ReviewIssueRenderer.Render(ReviewCandidates.Troubled()).Body;

        body.ShouldNotContain("@octocat");
        body.ShouldNotContain("@team");
        body.ShouldNotContain("<img");
        body.ShouldNotContain("\n# heading");
        body.ShouldContain("@&#8203;octocat");
        body.ShouldContain("\\| \\*\\*A.\\*\\*");
    }

    [Fact]
    public void Output_is_deterministic_and_uses_lf_line_endings()
    {
        var first = ReviewIssueRenderer.Render(ReviewCandidates.Troubled());
        var second = ReviewIssueRenderer.Render(ReviewCandidates.Troubled());

        second.ShouldBe(first);
        first.Body.ShouldNotContain('\r');
    }

    [Fact]
    public void Agenda_changed_comment_says_so_when_no_item_matches_any_more()
    {
        var candidate = ReviewCandidates.Hearing() with { Items = [] };

        var comment = ReviewIssueRenderer.RenderAgendaChanged(candidate, Sha256Hash.Compute("old"u8));

        comment.ShouldContain("No agenda item matches the keyword rules any more. Check the agenda before publishing.");
        comment.ShouldNotContain("| Agenda no. |");
    }

    [Theory]
    [InlineData("guilford", "Board of Commissioners", "guilford-board-of-commissioners-2026-11-05")]
    [InlineData("guilford", "Board of Commissioners - Work Session", "guilford-board-of-commissioners-work-session-2026-11-05")]
    [InlineData("winston-salem", "  City Council  ", "winston-salem-city-council-2026-11-05")]
    public void Event_slug_is_lowercase_words_and_single_hyphens(string jurisdictionId, string body, string expected)
    {
        ReviewIssueRenderer.EventSlug(jurisdictionId, body, new DateOnly(2026, 11, 5)).ShouldBe(expected);
    }
}
