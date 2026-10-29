using DcLedger.Domain;

namespace DcLedger.Application;

/// <summary>
/// Use case behind <c>dcl ingest</c> (issue #19). Reads one jurisdiction's meetings from its platform. For every
/// meeting with a published agenda it records when ingest first saw it (T for the review SLA, SPEC AC9). Only when an
/// agenda item matches the keyword rules is the agenda downloaded, by a route the platform's robots.txt allows (the
/// adapter knows which), and kept in the private bucket; the review issue then opens in the same run, and later changes
/// to the agenda's bytes comment on it. Each run is logged with the documents it retrieved (WS-AC3). Running twice in a
/// row opens nothing new (WS-AC2).
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
            foreach (var meeting in meetings)
            {
                progress.Meetings.Add(await IngestMeetingAsync(source, request, meeting, meetings, progress, cancellationToken).ConfigureAwait(false));
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
        Progress progress,
        CancellationToken cancellationToken)
    {
        var result = new MeetingResult { Meeting = meeting.Key, Date = meeting.Date, Body = meeting.Body, Outcome = MeetingOutcome.NoAgenda };
        if (meeting.AgendaUri is null)
        {
            return result;
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
            var state = new MeetingState { FirstSeenUtc = firstSeen, LastAgendaSha256 = document.Sha256, ReviewIssueNumber = opened };
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
        return result with { Outcome = MeetingOutcome.AgendaChanged, IssueNumber = issueNumber, Reopened = reopened };
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

    /// <summary>What a run has done so far; kept when it stops early.</summary>
    private sealed class Progress
    {
        public List<MeetingResult> Meetings { get; } = [];

        public List<RetrievedDocument> Documents { get; } = [];

        public List<string> Errors { get; } = [];
    }
}
