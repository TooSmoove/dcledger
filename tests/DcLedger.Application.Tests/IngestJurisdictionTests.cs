using System.Text;
using DcLedger.Domain;
using DcLedger.Testing;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

public sealed class IngestJurisdictionTests
{
    private const string PrivateTitle = "Rezoning for a data center campus near Jane Example's farm";

    private static readonly Jurisdiction Guilford = new()
    {
        Id = "guilford",
        Name = "Guilford County",
        Platform = Platform.Legistar,
        LegistarClient = "guilford",
        Bodies = ["Board of Commissioners"],
    };

    private static readonly DateTimeOffset Morning = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero); // 8:00 a.m. Eastern
    private static readonly DateOnly October15 = new(2026, 10, 15);
    private static readonly Uri HearingAgenda = new("https://guilford.legistar1.com/guilford/meetings/2026/10/1599_A_Agenda.pdf");
    private static readonly Uri OtherAgenda = new("https://guilford.legistar1.com/guilford/meetings/2026/10/1631_A_Addendum.pdf");

    private readonly FixedTime _time = new(Morning);
    private readonly FakeSource _source = new();
    private readonly FakeFetcher _fetcher;
    private readonly MemoryDocumentStore _documents = new();
    private readonly MemoryIngestState _state = new();
    private readonly MemoryReviewQueue _queue = new();
    private readonly MemoryRunLog _runLog = new();

    public IngestJurisdictionTests()
    {
        _fetcher = new FakeFetcher(_time);
        _source.Fetcher = _fetcher;
        _fetcher.Responses[HearingAgenda] = (Encoding.UTF8.GetBytes("%PDF-1.7 agenda, first copy"), "application/pdf");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task First_run_opens_one_issue_stores_the_agenda_and_logs_it()
    {
        _source.Meetings.Add(Hearing());

        var report = await RunAsync();

        report.Succeeded.ShouldBeTrue();
        var meeting = report.Meetings.ShouldHaveSingleItem();
        meeting.Outcome.ShouldBe(MeetingOutcome.IssueOpened);
        meeting.IssueNumber.ShouldBe(1);
        meeting.Rules.ShouldHaveSingleItem().ShouldBe("data_center");

        var issue = _queue.Opened.ShouldHaveSingleItem();
        issue.Title.ShouldBe("Review: Guilford County, Board of Commissioners, 2026-10-15 (case 2026-439)");
        issue.Body.ShouldNotContain("Rezoning");
        issue.Body.ShouldNotContain("Jane Example");

        var agenda = Sha256Hash.Compute(_fetcher.Responses[HearingAgenda].Content);
        _documents.Documents.Keys.ShouldHaveSingleItem().ShouldBe(agenda);
        _state.States[Key("1599")].ShouldBe(new MeetingState
        {
            FirstSeenUtc = Morning,
            LastAgendaSha256 = agenda,
            ReviewIssueNumber = 1,
            LastSeen = new MeetingSighting { Date = October15, Time = new TimeOnly(17, 30), AgendaPublished = true },
        });
        _state.Tracked["guilford"].ShouldHaveSingleItem().ShouldBe(new TrackedMeeting
        {
            Meeting = Key("1599"),
            Body = "Board of Commissioners",
            Date = October15,
            MeetingPageUri = Hearing().MeetingPageUri,
        });

        var run = _runLog.Runs.ShouldHaveSingleItem();
        run.RunId.ShouldBe("40623358875-1");
        run.JurisdictionId.ShouldBe("guilford");
        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        var document = run.Documents.ShouldHaveSingleItem();
        document.Meeting.ShouldBe(Key("1599"));
        document.Uri.ShouldBe(HearingAgenda);
        document.Sha256.ShouldBe(agenda);
        document.MediaType.ShouldBe("application/pdf");
        document.Created.ShouldBeTrue();
    }

    [Fact]
    public async Task The_window_is_30_days_back_to_60_days_ahead_in_eastern_dates()
    {
        _time.Now = new DateTimeOffset(2026, 11, 1, 2, 30, 0, TimeSpan.Zero); // still Oct 31 in Eastern time

        var report = await RunAsync();

        (report.FirstDate, report.LastDate).ShouldBe((new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 30)));
        _source.Windows.ShouldHaveSingleItem().ShouldBe((new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public async Task Second_run_opens_nothing_and_finds_the_same_hash()
    {
        _source.Meetings.Add(Hearing());
        await RunAsync();
        _time.Now = Morning.AddHours(6);

        var report = await RunAsync(runId: "40623358876-1");

        report.Succeeded.ShouldBeTrue();
        report.Meetings.ShouldHaveSingleItem().Outcome.ShouldBe(MeetingOutcome.Unchanged);
        _queue.Opened.Count.ShouldBe(1);
        _queue.Comments.ShouldBeEmpty();
        _documents.Writes.ShouldBe(1);
        var (first, second) = (_runLog.Runs[0].Documents.ShouldHaveSingleItem(), _runLog.Runs[1].Documents.ShouldHaveSingleItem());
        second.Sha256.ShouldBe(first.Sha256);
        second.Created.ShouldBeFalse();
    }

    [Fact]
    public async Task Changed_agenda_comments_on_the_issue_and_reopens_it()
    {
        _source.Meetings.Add(Hearing());
        await RunAsync();
        var before = _state.States[Key("1599")].LastAgendaSha256!.Value;
        _queue.Closed.Add(1);
        _fetcher.Responses[HearingAgenda] = (Encoding.UTF8.GetBytes("%PDF-1.7 agenda, republished"), "application/pdf");

        var report = await RunAsync();

        var meeting = report.Meetings.ShouldHaveSingleItem();
        meeting.Outcome.ShouldBe(MeetingOutcome.AgendaChanged);
        meeting.IssueNumber.ShouldBe(1);
        meeting.Reopened.ShouldBeTrue();
        var after = Sha256Hash.Compute(_fetcher.Responses[HearingAgenda].Content);
        var comment = _queue.Comments.ShouldHaveSingleItem();
        comment.Issue.ShouldBe(1);
        comment.Body.ShouldContain($"<!-- dc-ledger:agenda-changed {after.Value} -->");
        comment.Body.ShouldContain($"(was `{before.Value}`)");
        _state.States[Key("1599")].LastAgendaSha256.ShouldBe(after);
        _state.States[Key("1599")].FirstSeenUtc.ShouldBe(Morning);
        _queue.Opened.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Agenda_without_a_match_is_not_downloaded_and_only_its_first_sighting_is_kept()
    {
        _source.Meetings.Add(Hearing() with { Items = [Item("2", "Budget amendment")] });
        await RunAsync();
        _time.Now = Morning.AddHours(6);

        var report = await RunAsync();

        report.Meetings.ShouldHaveSingleItem().Outcome.ShouldBe(MeetingOutcome.NoMatch);
        _fetcher.Requests.ShouldBeEmpty();
        _state.States[Key("1599")].ShouldBe(new MeetingState { FirstSeenUtc = Morning });
        _queue.Opened.ShouldBeEmpty();
        _runLog.Runs.Count.ShouldBe(2);
        _runLog.Runs.ShouldAllBe(run => run.Documents.Count == 0 && run.Outcome == RunOutcome.Succeeded);
    }

    [Fact]
    public async Task Meeting_without_an_agenda_is_left_alone()
    {
        _source.Meetings.Add(Hearing() with { AgendaUri = null });

        var report = await RunAsync();

        report.Meetings.ShouldHaveSingleItem().Outcome.ShouldBe(MeetingOutcome.NoAgenda);
        _state.States.ShouldBeEmpty();
        _fetcher.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Issue_for_a_match_added_later_keeps_the_first_sighting_of_the_agenda()
    {
        _source.Meetings.Add(Hearing() with { Items = [Item("2", "Budget amendment")] });
        await RunAsync();
        _time.Now = Morning.AddHours(6);
        _source.Meetings[0] = Hearing();

        await RunAsync();

        _state.States[Key("1599")].FirstSeenUtc.ShouldBe(Morning);
        _queue.Opened.ShouldHaveSingleItem().Body.ShouldContain("| First seen | 2026-10-28T12:00:00Z");
    }

    [Fact]
    public async Task Other_meetings_in_the_same_slot_are_named_in_the_issue()
    {
        _source.Meetings.Add(Hearing());
        _source.Meetings.Add(Hearing() with { Key = Key("1631"), AgendaUri = OtherAgenda, Items = [Item("9", "Addendum")] });
        _source.Meetings.Add(Hearing() with { Key = Key("1630"), Time = new TimeOnly(15, 30), Items = [Item("8", "Work session")] });

        await RunAsync();

        var body = _queue.Opened.ShouldHaveSingleItem().Body;
        body.ShouldContain("Legistar also lists meeting 1631 for the same body, date and time"); // one sibling: 1630 is at 3:30
    }

    [Fact]
    public async Task Agenda_that_cannot_be_downloaded_fails_only_its_meeting()
    {
        _source.Meetings.Add(Hearing() with { Key = Key("1631"), AgendaUri = OtherAgenda });
        _source.Meetings.Add(Hearing());

        var report = await RunAsync();

        report.Succeeded.ShouldBeFalse();
        var failed = report.Meetings[0];
        failed.Outcome.ShouldBe(MeetingOutcome.Failed);
        failed.Error.ShouldNotBeNull().ShouldContain("Meeting 1631: the agenda couldn't be downloaded.");
        report.Errors.ShouldHaveSingleItem().ShouldBe(failed.Error);
        report.Meetings[1].Outcome.ShouldBe(MeetingOutcome.IssueOpened);
        _state.States[Key("1631")].ShouldBe(new MeetingState { FirstSeenUtc = Morning });
        _runLog.Runs.ShouldHaveSingleItem().Outcome.ShouldBe(RunOutcome.Failed);
    }

    [Fact]
    public async Task Source_failure_ends_the_run_and_is_logged()
    {
        _source.Failure = new SourceFailedException(SourceFailure.Unavailable, new Uri("https://webapi.legistar.com/v1/guilford/events"), "Legistar: HTTP 503 after retries.");

        var report = await RunAsync();

        report.Succeeded.ShouldBeFalse();
        report.Errors.ShouldHaveSingleItem().ShouldBe("Legistar: HTTP 503 after retries.");
        report.Meetings.ShouldBeEmpty();
        var run = _runLog.Runs.ShouldHaveSingleItem();
        (run.Outcome, run.Errors.ShouldHaveSingleItem()).ShouldBe((RunOutcome.Failed, "Legistar: HTTP 503 after retries."));
    }

    [Fact]
    public async Task Review_queue_failure_stops_the_run_and_a_retry_keeps_the_first_sighting()
    {
        _source.Meetings.Add(Hearing());
        _queue.Failure = new ReviewQueueFailedException("GitHub answered 502.", 502);

        var report = await RunAsync();

        report.Errors.ShouldHaveSingleItem().ShouldBe("GitHub answered 502.");
        _state.States[Key("1599")].ShouldBe(new MeetingState { FirstSeenUtc = Morning });

        _queue.Failure = null;
        _time.Now = Morning.AddHours(6);
        var retry = await RunAsync();

        retry.Meetings.ShouldHaveSingleItem().Outcome.ShouldBe(MeetingOutcome.IssueOpened);
        _state.States[Key("1599")].FirstSeenUtc.ShouldBe(Morning);
    }

    [Fact]
    public async Task Bucket_failure_stops_the_run_before_any_issue_opens()
    {
        _source.Meetings.Add(Hearing());
        _documents.Failure = new StorageFailedException("docs/sha256/ab/abc", "Couldn't write docs/sha256/ab/abc in the private bucket: HTTP 500");

        var report = await RunAsync();

        report.Errors.ShouldHaveSingleItem().ShouldContain("Couldn't write");
        _queue.Opened.ShouldBeEmpty();
        _runLog.Runs.ShouldHaveSingleItem().Outcome.ShouldBe(RunOutcome.Failed);
    }

    [Fact]
    public async Task Run_log_failure_is_reported_but_does_not_fail_the_run()
    {
        _source.Meetings.Add(Hearing());
        _runLog.Failure = new StorageFailedException("runs/2026/10/guilford/1.json", "Couldn't write the run log.");

        var report = await RunAsync();

        report.Succeeded.ShouldBeTrue();
        report.RunLogError.ShouldBe("Couldn't write the run log.");
    }

    [Fact]
    public async Task Dry_run_downloads_matching_agendas_but_writes_nothing()
    {
        _source.Meetings.Add(Hearing());
        _source.Meetings.Add(Hearing() with { Key = Key("1630"), Items = [Item("8", "Work session")] });

        var report = await RunAsync(dryRun: true);

        report.Succeeded.ShouldBeTrue();
        report.DryRun.ShouldBeTrue();
        var hearing = report.Meetings[0];
        hearing.Outcome.ShouldBe(MeetingOutcome.WouldOpenIssue);
        hearing.IssueTitle.ShouldBe("Review: Guilford County, Board of Commissioners, 2026-10-15 (case 2026-439)");
        report.Meetings[1].Outcome.ShouldBe(MeetingOutcome.NoMatch);
        report.Documents.ShouldHaveSingleItem().Created.ShouldBeFalse();
        _fetcher.Requests.ShouldHaveSingleItem().ShouldBe(HearingAgenda);
        _documents.Documents.ShouldBeEmpty();
        _state.States.ShouldBeEmpty();
        _queue.Opened.ShouldBeEmpty();
        _runLog.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_delay_is_measured_from_the_scheduled_slot()
    {
        var late = await RunAsync(scheduledFor: Morning.AddMinutes(-12));
        var early = await RunAsync(scheduledFor: Morning.AddMinutes(1));
        var byHand = await RunAsync();

        late.StartDelay.ShouldBe(TimeSpan.FromMinutes(12));
        _runLog.Runs[0].StartDelay.ShouldBe(TimeSpan.FromMinutes(12));
        early.StartDelay.ShouldBe(TimeSpan.Zero);
        byHand.StartDelay.ShouldBeNull();
    }

    [Fact]
    public async Task Tracked_meeting_whose_match_disappears_still_reports_agenda_changes()
    {
        _source.Meetings.Add(Hearing());
        await RunAsync();
        _source.Meetings[0] = Hearing() with { Items = [Item("2", "Budget amendment")] };
        _fetcher.Responses[HearingAgenda] = (Encoding.UTF8.GetBytes("%PDF-1.7 agenda without the hearing"), "application/pdf");

        var report = await RunAsync();

        report.Meetings.ShouldHaveSingleItem().Outcome.ShouldBe(MeetingOutcome.AgendaChanged);
        _queue.Comments.ShouldHaveSingleItem().Body.ShouldContain("No agenda item matches the keyword rules any more.");
    }

    [Fact]
    public async Task Rescheduled_meeting_comments_once_and_reopens_the_issue()
    {
        _source.Meetings.Add(Hearing());
        await RunAsync();
        _queue.Closed.Add(1);
        _source.Meetings[0] = Hearing() with
        {
            Date = new DateOnly(2026, 10, 22),
            Time = new TimeOnly(18, 0),
            StartsAt = new DateTimeOffset(2026, 10, 22, 18, 0, 0, TimeSpan.FromHours(-4)),
        };

        var report = await RunAsync();
        var again = await RunAsync();

        var meeting = report.Meetings.ShouldHaveSingleItem();
        meeting.Outcome.ShouldBe(MeetingOutcome.Unchanged, "the agenda's bytes didn't change");
        meeting.Changes.ShouldHaveSingleItem().ShouldBe(MeetingChange.Rescheduled);
        meeting.Reopened.ShouldBeTrue();
        var comment = _queue.Comments.ShouldHaveSingleItem();
        comment.Issue.ShouldBe(1);
        comment.Body.ShouldContain("**Meeting rescheduled**");
        comment.Body.ShouldContain("| Was | Thursday, October 15, 2026 at 5:30 PM |");
        comment.Body.ShouldContain("| Now | Thursday, October 22, 2026 at 6:00 PM |");
        again.Meetings.ShouldHaveSingleItem().Changes.ShouldBeEmpty();
        _state.States[Key("1599")].LastSeen!.Date.ShouldBe(new DateOnly(2026, 10, 22));
        _state.Tracked["guilford"].ShouldHaveSingleItem().Date.ShouldBe(new DateOnly(2026, 10, 22));
    }

    [Fact]
    public async Task Cancellation_hint_asks_the_reviewer_and_marks_nothing()
    {
        _source.Meetings.Add(Hearing());
        await RunAsync();
        _source.Meetings[0] = Hearing() with { PossiblyCancelled = true };

        var report = await RunAsync();
        await RunAsync();

        report.Meetings.ShouldHaveSingleItem().Changes.ShouldHaveSingleItem().ShouldBe(MeetingChange.CancellationMentioned);
        var comment = _queue.Comments.ShouldHaveSingleItem();
        comment.Body.ShouldContain("**Possible cancellation**");
        comment.Body.ShouldContain("set `status: cancelled` and raise `sequence` by 1");
        comment.Body.ShouldNotContain(PrivateTitle);
    }

    [Fact]
    public async Task Withdrawn_agenda_on_a_tracked_meeting_comments_without_downloading()
    {
        _source.Meetings.Add(Hearing());
        await RunAsync();
        var requests = _fetcher.Requests.Count;
        _source.Meetings[0] = Hearing() with { AgendaUri = null };

        var report = await RunAsync();

        var meeting = report.Meetings.ShouldHaveSingleItem();
        meeting.Outcome.ShouldBe(MeetingOutcome.NoAgenda);
        meeting.IssueNumber.ShouldBe(1);
        meeting.Changes.ShouldHaveSingleItem().ShouldBe(MeetingChange.AgendaWithdrawn);
        _queue.Comments.ShouldHaveSingleItem().Body.ShouldContain("The platform no longer lists a published agenda.");
        _fetcher.Requests.Count.ShouldBe(requests);
        _state.Tracked["guilford"].ShouldHaveSingleItem().Meeting.ShouldBe(Key("1599"), "still tracked without an agenda");
    }

    [Fact]
    public async Task Meeting_that_disappears_before_its_date_comments_once_then_again_when_relisted()
    {
        _source.Meetings.AddRange([Upcoming(), Unrelated()]);
        await RunAsync();
        _queue.Closed.Add(1);
        _source.Meetings.RemoveAt(0);

        var missing = await RunAsync();
        var stillMissing = await RunAsync();
        _source.Meetings.Insert(0, Upcoming());
        var back = await RunAsync();

        var gone = missing.Meetings.Single(result => result.Meeting == Key("1600"));
        gone.Outcome.ShouldBe(MeetingOutcome.NoLongerListed);
        gone.Changes.ShouldHaveSingleItem().ShouldBe(MeetingChange.NoLongerListed);
        gone.Reopened.ShouldBeTrue();
        var later = stillMissing.Meetings.Single(result => result.Meeting == Key("1600"));
        later.Outcome.ShouldBe(MeetingOutcome.NoLongerListed);
        later.Changes.ShouldBeEmpty();
        back.Meetings.Single(result => result.Meeting == Key("1600")).Changes.ShouldHaveSingleItem().ShouldBe(MeetingChange.ListedAgain);

        _queue.Comments.Count.ShouldBe(2);
        _queue.Comments[0].Body.ShouldContain("The platform no longer lists this meeting, and its date hasn't passed.");
        _queue.Comments[0].Body.ShouldContain("| Scheduled | Thursday, November 5, 2026 at 5:30 PM |");
        _queue.Comments[1].Body.ShouldContain("The platform lists this meeting again.");
        _state.States[Key("1600")].MissingSinceUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Meeting_missing_after_its_date_is_not_reported()
    {
        _source.Meetings.AddRange([Hearing(), Unrelated()]); // Oct 15, before the Oct 28 run
        await RunAsync();
        _source.Meetings.RemoveAt(0);

        var report = await RunAsync();

        report.Meetings.ShouldNotContain(result => result.Outcome == MeetingOutcome.NoLongerListed);
        _queue.Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Empty_meeting_list_is_never_read_as_every_meeting_removed()
    {
        _source.Meetings.Add(Upcoming());
        await RunAsync();
        _source.Meetings.Clear();

        var report = await RunAsync();

        report.Meetings.ShouldBeEmpty();
        _queue.Comments.ShouldBeEmpty();
        _state.Tracked["guilford"].ShouldHaveSingleItem().Meeting.ShouldBe(Key("1600"), "kept for when the source answers again");
    }

    [Fact]
    public async Task Source_failure_reports_no_disappearance()
    {
        _source.Meetings.AddRange([Upcoming(), Unrelated()]);
        await RunAsync();
        _source.Failure = new SourceFailedException(SourceFailure.Unavailable, null, "Legistar is down.");

        await RunAsync();

        _queue.Comments.ShouldBeEmpty();
        _state.States[Key("1600")].MissingSinceUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Issue_from_before_sightings_takes_a_silent_baseline()
    {
        var agenda = Sha256Hash.Compute(_fetcher.Responses[HearingAgenda].Content);
        _state.States[Key("1599")] = new MeetingState { FirstSeenUtc = Morning, LastAgendaSha256 = agenda, ReviewIssueNumber = 58 };
        _source.Meetings.Add(Hearing() with { PossiblyCancelled = true });

        var first = await RunAsync();
        _source.Meetings[0] = Hearing();
        var second = await RunAsync();

        first.Meetings.ShouldHaveSingleItem().Changes.ShouldBeEmpty();
        second.Meetings.ShouldHaveSingleItem().Changes.ShouldHaveSingleItem().ShouldBe(MeetingChange.CancellationMentionRemoved);
        var comment = _queue.Comments.ShouldHaveSingleItem();
        comment.Issue.ShouldBe(58);
        comment.Body.ShouldContain("**If it's back on:**");
    }

    [Fact]
    public async Task Dry_run_reads_and_writes_no_tracked_meetings()
    {
        _source.Meetings.Add(Upcoming());

        await RunAsync(dryRun: true);

        _state.Tracked.ShouldBeEmpty();
        _state.TrackedWrites.ShouldBe(0);
    }

    [Fact]
    public async Task Unchanged_tracked_meetings_are_not_rewritten()
    {
        _source.Meetings.Add(Upcoming());
        await RunAsync();

        await RunAsync();

        _state.TrackedWrites.ShouldBe(1);
    }

    [Fact]
    public async Task Jurisdiction_whose_platform_has_no_adapter_is_refused()
    {
        var manual = Guilford with { Id = "greensboro", Platform = Platform.Manual, LegistarClient = null };

        var exception = await Should.ThrowAsync<ArgumentException>(() => Ingest().RunAsync(new IngestRequest { Jurisdiction = manual, RunId = "1" }, Ct));

        exception.Message.ShouldContain("No source adapter reads manual jurisdictions such as greensboro.");
    }

    private Task<IngestReport> RunAsync(string runId = "40623358875-1", bool dryRun = false, DateTimeOffset? scheduledFor = null) =>
        Ingest().RunAsync(new IngestRequest { Jurisdiction = Guilford, RunId = runId, DryRun = dryRun, ScheduledForUtc = scheduledFor }, Ct);

    private IngestJurisdiction Ingest() => new([_source], _documents, _state, _queue, _runLog, _time);

    private static MeetingKey Key(string id) => new(Platform.Legistar, "guilford", id);

    private static SourceMeeting Hearing() => new()
    {
        Key = Key("1599"),
        Body = "Board of Commissioners",
        Date = October15,
        Time = new TimeOnly(17, 30),
        StartsAt = new DateTimeOffset(2026, 10, 15, 17, 30, 0, TimeSpan.FromHours(-4)),
        AgendaUri = HearingAgenda,
        MeetingPageUri = new Uri("https://guilford.legistar.com/MeetingDetail.aspx?LEGID=1599&GID=590"),
        Items = [Item("1", PrivateTitle, "2026-439"), Item("2", "Budget amendment")],
    };

    /// <summary>The Nov 5 meeting: after the Oct 28 run, so missing from the list means before its date.</summary>
    private static SourceMeeting Upcoming() => Hearing() with
    {
        Key = Key("1600"),
        Date = new DateOnly(2026, 11, 5),
        StartsAt = new DateTimeOffset(2026, 11, 5, 17, 30, 0, TimeSpan.FromHours(-5)),
        MeetingPageUri = new Uri("https://guilford.legistar.com/MeetingDetail.aspx?LEGID=1600&GID=590"),
    };

    /// <summary>A meeting with a published agenda and no match, so the source's list isn't empty.</summary>
    private static SourceMeeting Unrelated() => Hearing() with
    {
        Key = Key("1601"),
        Date = new DateOnly(2026, 11, 19),
        StartsAt = new DateTimeOffset(2026, 11, 19, 17, 30, 0, TimeSpan.FromHours(-5)),
        AgendaUri = new Uri("https://guilford.legistar1.com/guilford/meetings/2026/11/1601_A_Agenda.pdf"),
        Items = [Item("3", "Budget amendment")],
    };

    private static SourceItem Item(string id, string title, string? caseNumber = null) => new()
    {
        ItemId = id,
        AgendaNumber = "A.",
        Title = title,
        CaseNumber = caseNumber,
        MatterType = "Public Hearing",
    };
}
