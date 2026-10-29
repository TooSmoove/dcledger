// In-memory stand-ins for the ingest ports, compiled into the Application and CLI test projects (linked from their
// project files), so the use case and the dcl ingest command are tested against the same fakes.
using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Testing;

/// <summary>A clock that only moves when a test moves it.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
}

/// <summary>
/// Returns the meetings a test sets, records each window asked for, or throws <see cref="Failure"/>. Agendas are read
/// from <see cref="Fetcher"/> at the meeting's agenda address, and fetch failures become source failures.
/// </summary>
internal sealed class FakeSource : ISourceAdapter
{
    public Platform Platform { get; init; } = Platform.Legistar;

    public List<SourceMeeting> Meetings { get; } = [];

    public List<(DateOnly Start, DateOnly End)> Windows { get; } = [];

    public SourceFailedException? Failure { get; set; }

    public IFetcher? Fetcher { get; set; }

    public Task<IReadOnlyList<SourceMeeting>> GetMeetingsAsync(Jurisdiction jurisdiction, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        Windows.Add((startDate, endDate));
        return Failure is null ? Task.FromResult<IReadOnlyList<SourceMeeting>>([.. Meetings]) : Task.FromException<IReadOnlyList<SourceMeeting>>(Failure);
    }

    public async Task<FetchedResource> GetAgendaAsync(SourceMeeting meeting, CancellationToken cancellationToken)
    {
        var fetcher = Fetcher ?? throw new InvalidOperationException("Set Fetcher to serve agendas.");
        try
        {
            return await fetcher.GetAsync(meeting.AgendaUri!, cancellationToken);
        }
        catch (FetchFailedException exception)
        {
            throw new SourceFailedException(SourceFailure.Rejected, exception.Uri, $"Fake: {exception.Message}", exception);
        }
    }
}

/// <summary>Serves bytes per URL, stamped with the clock's time; unknown URLs answer HTTP 404. Records every request.</summary>
internal sealed class FakeFetcher(TimeProvider time) : IFetcher
{
    public Dictionary<Uri, (byte[] Content, string? MediaType)> Responses { get; } = new();

    /// <summary>Answers URLs that aren't in <see cref="Responses"/>; null means HTTP 404.</summary>
    public Func<Uri, (byte[] Content, string? MediaType)?>? Fallback { get; set; }

    public List<Uri> Requests { get; } = [];

    public Task<FetchedResource> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        Requests.Add(uri);
        if (!Responses.TryGetValue(uri, out var response))
        {
            if (Fallback?.Invoke(uri) is not { } fallback)
            {
                return Task.FromException<FetchedResource>(new FetchFailedException(uri, FetchFailure.HttpStatus, $"{uri} answered HTTP 404.", 404));
            }

            response = fallback;
        }

        return Task.FromResult(new FetchedResource(response.Content, response.MediaType, uri, time.GetUtcNow(), Sha256Hash.Compute(response.Content)));
    }
}

/// <summary>Documents by hash; never rewrites, like the bucket.</summary>
internal sealed class MemoryDocumentStore : IDocumentStore
{
    public Dictionary<Sha256Hash, (byte[] Content, string? MediaType)> Documents { get; } = new();

    public int Writes { get; private set; }

    public StorageFailedException? Failure { get; set; }

    public Task<StoredDocument> PutAsync(ReadOnlyMemory<byte> content, string? mediaType, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException<StoredDocument>(Failure);
        }

        var sha256 = Sha256Hash.Compute(content.Span);
        var created = Documents.TryAdd(sha256, (content.ToArray(), mediaType));
        Writes += created ? 1 : 0;
        return Task.FromResult(new StoredDocument(sha256, "docs/sha256/" + sha256.Value, created));
    }
}

/// <summary>Ingest state in a dictionary.</summary>
internal sealed class MemoryIngestState : IIngestState
{
    public Dictionary<MeetingKey, MeetingState> States { get; } = new();

    public Task<MeetingState?> GetAsync(MeetingKey meeting, CancellationToken cancellationToken) =>
        Task.FromResult(States.TryGetValue(meeting, out var state) ? state : null);

    public Task PutAsync(MeetingKey meeting, MeetingState state, CancellationToken cancellationToken)
    {
        States[meeting] = state;
        return Task.CompletedTask;
    }
}

/// <summary>Issues numbered from 1; a test can close one to check that a comment reopens it.</summary>
internal sealed class MemoryReviewQueue : IReviewQueue
{
    public List<ReviewIssue> Opened { get; } = [];

    public List<(int Issue, string Body)> Comments { get; } = [];

    public HashSet<int> Closed { get; } = [];

    public ReviewQueueFailedException? Failure { get; set; }

    public Task<int> OpenAsync(ReviewIssue issue, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException<int>(Failure);
        }

        Opened.Add(issue);
        return Task.FromResult(Opened.Count);
    }

    public Task<bool> CommentAsync(int issueNumber, string body, CancellationToken cancellationToken)
    {
        Comments.Add((issueNumber, body));
        return Task.FromResult(Closed.Remove(issueNumber));
    }
}

/// <summary>Run records in a list, or a write failure.</summary>
internal sealed class MemoryRunLog : IRunLog
{
    public List<RunRecord> Runs { get; } = [];

    public StorageFailedException? Failure { get; set; }

    public Task WriteAsync(RunRecord run, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Runs.Add(run);
        return Task.CompletedTask;
    }
}
