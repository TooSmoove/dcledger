using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>
/// Use case behind <c>dcl ingest</c> (issue #19). Reads one jurisdiction's meetings from its platform. For every
/// meeting with a published agenda it records when ingest first saw it (T for the review SLA, SPEC AC9). Only when an
/// agenda item matches the keyword rules is the agenda downloaded, by a route the platform's robots.txt allows (the
/// adapter knows which), and kept in the private bucket; the review issue then opens in the same run, and later changes
/// to the agenda's bytes comment on it. Each run is logged with the documents it retrieved (WS-AC3). Running twice in a
/// row opens nothing new (WS-AC2).
/// <para>
/// A meeting with an issue is also watched for changes to the meeting itself (issue #29): a new date or time, a comment
/// that mentions a cancellation, a withdrawn agenda, or the platform no longer listing it before its date. Each change
/// comments on the issue once, reopening it. The platform has no cancellation status (spike S15), so nothing is marked
/// cancelled here; the reviewer edits the ledger.
/// </para>
/// </summary>
public sealed class IngestJurisdiction
{
    /// <summary>Days before today (Eastern) that are scanned, so agendas republished after a meeting are noticed.</summary>
    public const int DaysBack = 30;

    /// <summary>Days after today (Eastern) that are scanned; the radar itself shows 30 (SPEC AC1).</summary>
    public const int DaysAhead = 60;

    private const string UnknownMediaType = "application/octet-stream";

    private readonly IReadOnlyList<ISourceAdapter> _sources;
    private readonly IDocumentStore _documents;
    private readonly IIngestState _state;
    private readonly IReviewQueue _queue;
    private readonly IRunLog _runLog;
    private readonly TimeProvider _time;

    /// <summary>Creates the use case. <paramref name="sources"/> holds one adapter per platform.</summary>
    public IngestJurisdiction(
        IEnumerable<ISourceAdapter> sources,
        IDocumentStore documents,
        IIngestState state,
        IReviewQueue queue,
        IRunLog runLog,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = [.. sources];
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _runLog = runLog ?? throw new ArgumentNullException(nameof(runLog));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Ingests one jurisdiction. When the source, the bucket or the review queue fails after retries the run stops and
    /// the failure is in <see cref="IngestReport.Errors"/>; an agenda that can't be downloaded fails only its meeting.
    /// Bugs propagate. The run log is written unless this is a dry run. Throws <see cref="ArgumentException"/> when no
    /// adapter reads the jurisdiction's platform.
    /// </summary>
    public async Task<IngestReport> RunAsync(IngestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var jurisdiction = request.Jurisdiction;
        var source = _sources.FirstOrDefault(adapter => adapter.Platform == jurisdiction.Platform)
            ?? throw new ArgumentException($"No source adapter reads {jurisdiction.Platform.ToToken()} jurisdictions such as {jurisdiction.Id}.", nameof(request));

        var started = _time.GetUtcNow();
        var today = DateOnly.FromDateTime(EasternTime.ToEastern(started).DateTime);
        var firstDate = today.AddDays(-DaysBack);
        var lastDate = today.AddDays(DaysAhead);
        var progress = new Progress();
        try
        {
            var meetings = await source.GetMeetingsAsync(jurisdiction, firstDate, lastDate.AddDays(1), cancellationToken).ConfigureAwait(false);
            IReadOnlyList<TrackedMeeting> tracked = [];
            if (!request.DryRun)
            {
                tracked = await _state.GetTrackedAsync(jurisdiction.Id, cancellationToken).ConfigureAwait(false);
            }

            var trackedKeys = tracked.Select(entry => entry.Meeting).ToHashSet();
            foreach (var meeting in meetings)
            {
                var isTracked = trackedKeys.Contains(meeting.Key);
                progress.Meetings.Add(await IngestMeetingAsync(source, request, meeting, meetings, isTracked, progress, cancellationToken).ConfigureAwait(false));
            }

            if (!request.DryRun)
            {
                await CheckUnlistedAsync(jurisdiction, tracked, meetings, today, lastDate, progress, cancellationToken).ConfigureAwait(false);
                await SaveTrackedAsync(jurisdiction.Id, tracked, meetings, firstDate, progress, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SourceFailedException exception)
        {
            progress.Errors.Add(exception.Message);
        }
        catch (StorageFailedException exception)
        {
            progress.Errors.Add(exception.Message);
        }
        catch (ReviewQueueFailedException exception)
        {
            progress.Errors.Add(exception.Message);
        }

        TimeSpan? startDelay = request.ScheduledForUtc is { } slot ? Max(started - slot, TimeSpan.Zero) : null;
        var runLogError = request.DryRun ? null : await WriteRunLogAsync(request, started, startDelay, progress, cancellationToken).ConfigureAwait(false);
        return new IngestReport
        {
            JurisdictionId = jurisdiction.Id,
            FirstDate = firstDate,
            LastDate = lastDate,
            DryRun = request.DryRun,
            StartDelay = startDelay,
            Meetings = [.. progress.Meetings],
            Documents = [.. progress.Documents],
            Errors = [.. progress.Errors],
            RunLogError = runLogError,
        };
    }

    private async Task<MeetingResult> IngestMeetingAsync(
        ISourceAdapter source,
        IngestRequest request,
        SourceMeeting meeting,
        IReadOnlyList<SourceMeeting> meetings,
        bool isTracked,
        Progress progress,
        CancellationToken cancellationToken)
    {
        var result = new MeetingResult { Meeting = meeting.Key, Date = meeting.Date, Body = meeting.Body, Outcome = MeetingOutcome.NoAgenda };
        if (meeting.AgendaUri is null)
        {
            // A tracked meeting whose agenda was withdrawn is still watched; an untracked one has no review clock yet.
            return isTracked && !request.DryRun
                ? await TrackedWithoutAgendaAsync(request.Jurisdiction, meeting, result, cancellationToken).ConfigureAwait(false)
                : result;
        }

        var matches = Matches(meeting);
        result = result with { Rules = [.. MeetingItemClassifier.RuleNames.Where(rule => matches.Any(match => match.Classification.MatchedRules.Contains(rule)))] };
        var known = request.DryRun ? null : await _state.GetAsync(meeting.Key, cancellationToken).ConfigureAwait(false);
        var firstSeen = known?.FirstSeenUtc ?? _time.GetUtcNow();
        if (!request.DryRun && known is null)
        {
            // Recorded before anything else can fail, so a retry next run keeps the original review clock.
            await _state.PutAsync(meeting.Key, new MeetingState { FirstSeenUtc = firstSeen }, cancellationToken).ConfigureAwait(false);
        }

        // A meeting with an issue stays tracked even if its matching item later disappears from the agenda.
        if (matches.Count == 0 && known?.ReviewIssueNumber is null)
        {
            return result with { Outcome = MeetingOutcome.NoMatch };
        }

        if (known?.ReviewIssueNumber is { } watchedIssue)
        {
            var noted = await NoteSightingAsync(request.Jurisdiction, meeting, known, watchedIssue, cancellationToken).ConfigureAwait(false);
            known = noted.State;
            result = result with { Changes = noted.Changes, Reopened = noted.Reopened };
        }

        FetchedResource agenda;
        try
        {
            agenda = await source.GetAgendaAsync(meeting, cancellationToken).ConfigureAwait(false);
        }
        catch (SourceFailedException exception)
        {
            var error = $"Meeting {meeting.Key.MeetingId}: the agenda couldn't be downloaded. {exception.Message}";
            progress.Errors.Add(error);
            return result with { Outcome = MeetingOutcome.Failed, IssueNumber = known?.ReviewIssueNumber, Error = error };
        }

        var document = await StoreAsync(request.DryRun, meeting.Key, agenda, cancellationToken).ConfigureAwait(false);
        progress.Documents.Add(document);
        var candidate = ReviewCandidate.FromMeeting(
            request.Jurisdiction,
            meeting,
            new AgendaDocument(document.Sha256, document.Uri, document.MediaType, document.Bytes, document.RetrievedAtUtc),
            [.. matches.Select(match => ReviewItem.FromMatch(match.Item, match.Classification))],
            firstSeen,
            SameSlot(meeting, meetings));

        if (known?.ReviewIssueNumber is not { } issueNumber)
        {
            var issue = ReviewIssueRenderer.Render(candidate);
            if (request.DryRun)
            {
                return result with { Outcome = MeetingOutcome.WouldOpenIssue, IssueTitle = issue.Title };
            }

            var opened = await _queue.OpenAsync(issue, cancellationToken).ConfigureAwait(false);
            var state = new MeetingState
            {
                FirstSeenUtc = firstSeen,
                LastAgendaSha256 = document.Sha256,
                ReviewIssueNumber = opened,
                LastSeen = MeetingSighting.Of(meeting),
            };
            await _state.PutAsync(meeting.Key, state, cancellationToken).ConfigureAwait(false);
            return result with { Outcome = MeetingOutcome.IssueOpened, IssueNumber = opened, IssueTitle = issue.Title };
        }

        if (known.LastAgendaSha256 is not { } previous)
        {
            // Recorded without a hash (not written by this use case): adopt this copy as the baseline.
            await _state.PutAsync(meeting.Key, known with { LastAgendaSha256 = document.Sha256 }, cancellationToken).ConfigureAwait(false);
            return result with { Outcome = MeetingOutcome.Unchanged, IssueNumber = issueNumber };
        }

        if (previous == document.Sha256)
        {
            return result with { Outcome = MeetingOutcome.Unchanged, IssueNumber = issueNumber };
        }

        var reopened = await _queue.CommentAsync(issueNumber, ReviewIssueRenderer.RenderAgendaChanged(candidate, previous), cancellationToken).ConfigureAwait(false);
        await _state.PutAsync(meeting.Key, known with { LastAgendaSha256 = document.Sha256 }, cancellationToken).ConfigureAwait(false);
        return result with { Outcome = MeetingOutcome.AgendaChanged, IssueNumber = issueNumber, Reopened = result.Reopened || reopened };
    }

    private async Task<MeetingResult> TrackedWithoutAgendaAsync(Jurisdiction jurisdiction, SourceMeeting meeting, MeetingResult result, CancellationToken cancellationToken)
    {
        var known = await _state.GetAsync(meeting.Key, cancellationToken).ConfigureAwait(false);
        if (known?.ReviewIssueNumber is not { } issueNumber)
        {
            return result;
        }

        var noted = await NoteSightingAsync(jurisdiction, meeting, known, issueNumber, cancellationToken).ConfigureAwait(false);
        return result with { IssueNumber = issueNumber, Changes = noted.Changes, Reopened = noted.Reopened };
    }

    /// <summary>
    /// Compares the meeting with how it was last seen and comments on its issue when it changed. The first sighting of
    /// an issue that has none (opened before sightings were kept) becomes the baseline, without a comment. The state is
    /// written after the comment, so a failed write repeats the comment next run rather than losing it.
    /// </summary>
    private async Task<Noted> NoteSightingAsync(Jurisdiction jurisdiction, SourceMeeting meeting, MeetingState known, int issueNumber, CancellationToken cancellationToken)
    {
        var now = MeetingSighting.Of(meeting);
        if (known.LastSeen is not { } before)
        {
            var baseline = known with { LastSeen = now, MissingSinceUtc = null };
            await _state.PutAsync(meeting.Key, baseline, cancellationToken).ConfigureAwait(false);
            return new Noted(baseline, [], Reopened: false);
        }

        var changes = new List<MeetingChange>();
        if (known.MissingSinceUtc is not null)
        {
            changes.Add(MeetingChange.ListedAgain);
        }

        changes.AddRange(MeetingChanges.Between(before, now));
        if (changes.Count == 0)
        {
            return new Noted(known, [], Reopened: false);
        }

        var notice = new MeetingChangeNotice
        {
            Meeting = meeting.Key,
            JurisdictionName = jurisdiction.Name,
            Body = meeting.Body,
            MeetingPageUri = meeting.MeetingPageUri,
            Before = before,
            Now = now,
            Changes = changes,
            CheckedUtc = _time.GetUtcNow(),
        };
        var reopened = await _queue.CommentAsync(issueNumber, ReviewIssueRenderer.RenderMeetingChanged(notice), cancellationToken).ConfigureAwait(false);
        var updated = known with { LastSeen = now, MissingSinceUtc = null };
        await _state.PutAsync(meeting.Key, updated, cancellationToken).ConfigureAwait(false);
        return new Noted(updated, changes, reopened);
    }

    /// <summary>
    /// Comments once on each tracked meeting the platform stopped listing before its date (spike S15). A meeting whose
    /// date has passed may simply have aged out, and an empty list looks like a source fault, so neither is reported
    /// (absence is never read as removal, ADR-0008).
    /// </summary>
    private async Task CheckUnlistedAsync(
        Jurisdiction jurisdiction,
        IReadOnlyList<TrackedMeeting> tracked,
        IReadOnlyList<SourceMeeting> meetings,
        DateOnly today,
        DateOnly lastDate,
        Progress progress,
        CancellationToken cancellationToken)
    {
        if (meetings.Count == 0)
        {
            return;
        }

        var listed = meetings.Select(meeting => meeting.Key).ToHashSet();
        foreach (var entry in tracked.Where(candidate => !listed.Contains(candidate.Meeting) && candidate.Date >= today && candidate.Date <= lastDate))
        {
            var known = await _state.GetAsync(entry.Meeting, cancellationToken).ConfigureAwait(false);
            if (known?.ReviewIssueNumber is not { } issueNumber)
            {
                continue;
            }

            var result = new MeetingResult { Meeting = entry.Meeting, Date = entry.Date, Body = entry.Body, Outcome = MeetingOutcome.NoLongerListed, IssueNumber = issueNumber };
            if (known.MissingSinceUtc is not null)
            {
                progress.Meetings.Add(result);
                continue;
            }

            var checkedUtc = _time.GetUtcNow();
            var notice = new MeetingChangeNotice
            {
                Meeting = entry.Meeting,
                JurisdictionName = jurisdiction.Name,
                Body = entry.Body,
                MeetingPageUri = entry.MeetingPageUri,
                Before = known.LastSeen ?? new MeetingSighting { Date = entry.Date, AgendaPublished = true },
                Now = null,
                Changes = [MeetingChange.NoLongerListed],
                CheckedUtc = checkedUtc,
            };
            var reopened = await _queue.CommentAsync(issueNumber, ReviewIssueRenderer.RenderMeetingChanged(notice), cancellationToken).ConfigureAwait(false);
            await _state.PutAsync(entry.Meeting, known with { MissingSinceUtc = checkedUtc }, cancellationToken).ConfigureAwait(false);
            progress.Meetings.Add(result with { Changes = notice.Changes, Reopened = reopened });
        }
    }

    /// <summary>
    /// Records the jurisdiction's meetings with issues as listed now, keeping unlisted ones while their date is still
    /// in the scanned window so a relisting is noticed. Written only when it changed.
    /// </summary>
    private async Task SaveTrackedAsync(
        string jurisdictionId,
        IReadOnlyList<TrackedMeeting> tracked,
        IReadOnlyList<SourceMeeting> meetings,
        DateOnly firstDate,
        Progress progress,
        CancellationToken cancellationToken)
    {
        var withIssue = progress.Meetings
            .Where(result => result.IssueNumber is not null && result.Outcome != MeetingOutcome.NoLongerListed)
            .Select(result => result.Meeting)
            .ToHashSet();
        var listed = meetings.Select(meeting => meeting.Key).ToHashSet();
        var next = meetings
            .Where(meeting => withIssue.Contains(meeting.Key))
            .Select(meeting => new TrackedMeeting { Meeting = meeting.Key, Body = meeting.Body, Date = meeting.Date, MeetingPageUri = meeting.MeetingPageUri })
            .Concat(tracked.Where(entry => !listed.Contains(entry.Meeting) && entry.Date >= firstDate))
            .DistinctBy(entry => entry.Meeting)
            .OrderBy(entry => entry.Date)
            .ThenBy(entry => entry.Meeting.MeetingId, StringComparer.Ordinal)
            .ToList();
        if (!next.SequenceEqual(tracked))
        {
            await _state.PutTrackedAsync(jurisdictionId, next, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<RetrievedDocument> StoreAsync(bool dryRun, MeetingKey meeting, FetchedResource agenda, CancellationToken cancellationToken)
    {
        var sha256 = agenda.Sha256;
        var created = false;
        if (!dryRun)
        {
            var stored = await _documents.PutAsync(agenda.Content, agenda.MediaType, cancellationToken).ConfigureAwait(false);
            (sha256, created) = (stored.Sha256, stored.Created);
        }

        return new RetrievedDocument
        {
            Meeting = meeting,
            Uri = agenda.FinalUri,
            RetrievedAtUtc = agenda.RetrievedAtUtc,
            Sha256 = sha256,
            MediaType = string.IsNullOrWhiteSpace(agenda.MediaType) ? UnknownMediaType : agenda.MediaType,
            Bytes = agenda.Content.Length,
            Created = created,
        };
    }

    private async Task<string?> WriteRunLogAsync(IngestRequest request, DateTimeOffset started, TimeSpan? startDelay, Progress progress, CancellationToken cancellationToken)
    {
        var run = new RunRecord
        {
            RunId = request.RunId,
            JurisdictionId = request.Jurisdiction.Id,
            StartedUtc = started,
            FinishedUtc = _time.GetUtcNow(),
            StartDelay = startDelay,
            Outcome = progress.Errors.Count == 0 ? RunOutcome.Succeeded : RunOutcome.Failed,
            Errors = [.. progress.Errors],
            Documents = [.. progress.Documents],
        };
        try
        {
            await _runLog.WriteAsync(run, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (StorageFailedException exception)
        {
            return exception.Message;
        }
    }

    private static List<ItemMatch> Matches(SourceMeeting meeting) =>
        [.. meeting.Items
            .Select(item => new ItemMatch(item, MeetingItemClassifier.Classify(item.ClassifierText)))
            .Where(match => match.Classification.IsCandidate)];

    /// <summary>Other meetings of the same body at the same date and time; Legistar lists addenda as separate meetings.</summary>
    private static List<string> SameSlot(SourceMeeting meeting, IReadOnlyList<SourceMeeting> meetings) =>
        [.. meetings
            .Where(other => other.Key != meeting.Key && other.Body == meeting.Body && other.Date == meeting.Date && other.Time == meeting.Time)
            .Select(other => other.Key.MeetingId)];

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private sealed record ItemMatch(SourceItem Item, Classification Classification);

    /// <summary>The meeting's state after a sighting, what changed, and whether the comment reopened the issue.</summary>
    private sealed record Noted(MeetingState State, IReadOnlyList<MeetingChange> Changes, bool Reopened);

    /// <summary>What a run has done so far; kept when it stops early.</summary>
    private sealed class Progress
    {
        public List<MeetingResult> Meetings { get; } = [];

        public List<RetrievedDocument> Documents { get; } = [];

        public List<string> Errors { get; } = [];
    }
}
