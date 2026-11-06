using System.Text;
using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Infrastructure.Storage;
using Shouldly;
using Xunit;

namespace DcLedger.Infrastructure.Tests.Storage;

public sealed class BucketDocumentStoreTests
{
    private readonly InMemoryObjectStore _bucket = new();

    [Fact]
    public async Task New_document_is_written_under_its_hash()
    {
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7 agenda");
        var hash = Sha256Hash.Compute(bytes);

        var stored = await Store().PutAsync(bytes, "application/pdf", Ct);

        stored.ShouldBe(new StoredDocument(hash, StorageKeys.Document(hash), Created: true));
        _bucket.Objects[stored.Key].Content.ToArray().ShouldBe(bytes);
        _bucket.Objects[stored.Key].ContentType.ShouldBe("application/pdf");
    }

    [Fact]
    public async Task Same_bytes_again_are_checked_but_not_rewritten()
    {
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7 agenda");

        var first = await Store().PutAsync(bytes, "application/pdf", Ct);
        var second = await Store().PutAsync(bytes, "application/pdf", Ct);

        second.ShouldBe(first with { Created = false });
        string[] expected = ["HEAD " + first.Key, "PUT " + first.Key, "HEAD " + first.Key];
        _bucket.Operations.ShouldBe(expected);
    }

    [Fact]
    public async Task Missing_media_type_is_stored_as_octet_stream()
    {
        byte[] bytes = [1, 2, 3];

        var stored = await Store().PutAsync(bytes, null, Ct);

        _bucket.Objects[stored.Key].ContentType.ShouldBe("application/octet-stream");
    }

    private BucketDocumentStore Store() => new(_bucket);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}

public sealed class BucketIngestStateTests
{
    private const string Key = "state/legistar/guilford/1599.json";
    private const string TrackedKey = "state/tracked/guilford.json";
    private static readonly MeetingKey Meeting = new(Platform.Legistar, "guilford", "1599");
    private static readonly Sha256Hash Agenda = Sha256Hash.Compute("agenda"u8);

    private readonly InMemoryObjectStore _bucket = new();

    [Fact]
    public async Task Unknown_meeting_has_no_state()
    {
        (await State().GetAsync(Meeting, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task State_round_trips_and_is_kept_in_utc()
    {
        var firstSeenEastern = new DateTimeOffset(2026, 10, 28, 11, 0, 0, TimeSpan.FromHours(-4));
        var state = new MeetingState { FirstSeenUtc = firstSeenEastern, LastAgendaSha256 = Agenda, ReviewIssueNumber = 12 };

        await State().PutAsync(Meeting, state, Ct);
        var read = await State().GetAsync(Meeting, Ct);

        read.ShouldBe(state);
        read!.FirstSeenUtc.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task Stored_json_has_the_documented_shape()
    {
        var state = new MeetingState
        {
            FirstSeenUtc = new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero),
            LastAgendaSha256 = Agenda,
            ReviewIssueNumber = 12,
        };

        await State().PutAsync(Meeting, state, Ct);

        _bucket.Objects[Key].ContentType.ShouldBe("application/json");
        _bucket.Objects[Key].Text.ShouldBe(
            "{\"schema_version\":1,\"first_seen_utc\":\"2026-10-28T15:00:00+00:00\","
            + $"\"last_agenda_sha256\":\"{Agenda.Value}\",\"review_issue_number\":12}}");
    }

    [Fact]
    public async Task State_without_agenda_or_issue_round_trips()
    {
        var state = new MeetingState { FirstSeenUtc = new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero) };

        await State().PutAsync(Meeting, state, Ct);

        (await State().GetAsync(Meeting, Ct)).ShouldBe(state);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"schema_version\":1}")]
    [InlineData("{\"schema_version\":2,\"first_seen_utc\":\"2026-10-28T15:00:00+00:00\",\"last_agenda_sha256\":null,\"review_issue_number\":null}")]
    [InlineData("{\"schema_version\":1,\"first_seen_utc\":\"2026-10-28T15:00:00+00:00\",\"last_agenda_sha256\":\"ABC\",\"review_issue_number\":null}")]
    [InlineData("{\"schema_version\":1,\"first_seen_utc\":\"2026-10-28T15:00:00+00:00\",\"last_agenda_sha256\":null,\"review_issue_number\":null,\"title\":\"x\"}")]
    public async Task Invalid_state_is_a_storage_failure(string json)
    {
        _bucket.Seed(Key, json);

        var failure = await Should.ThrowAsync<StorageFailedException>(() => State().GetAsync(Meeting, Ct));

        failure.Key.ShouldBe(Key);
    }

    [Fact]
    public async Task Sighting_and_missing_time_round_trip_in_utc()
    {
        var state = new MeetingState
        {
            FirstSeenUtc = new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero),
            ReviewIssueNumber = 12,
            LastSeen = new MeetingSighting { Date = new DateOnly(2026, 11, 5), Time = new TimeOnly(17, 30), PossiblyCancelled = true, AgendaPublished = true },
            MissingSinceUtc = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.FromHours(-4)),
        };

        await State().PutAsync(Meeting, state, Ct);
        var read = await State().GetAsync(Meeting, Ct);

        read.ShouldBe(state);
        read!.MissingSinceUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
        _bucket.Objects[Key].Text.ShouldContain(
            "\"last_seen\":{\"date\":\"2026-11-05\",\"time\":\"17:30:00\",\"possibly_cancelled\":true,\"agenda_published\":true},"
            + "\"missing_since_utc\":\"2026-11-01T12:00:00+00:00\"");
    }

    [Fact]
    public async Task Sighting_without_a_start_time_round_trips()
    {
        var state = new MeetingState
        {
            FirstSeenUtc = new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero),
            LastSeen = new MeetingSighting { Date = new DateOnly(2026, 11, 5) },
        };

        await State().PutAsync(Meeting, state, Ct);

        (await State().GetAsync(Meeting, Ct)).ShouldBe(state);
    }

    [Fact]
    public async Task No_tracked_meetings_until_some_are_written()
    {
        (await State().GetTrackedAsync("guilford", Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Tracked_meetings_round_trip_with_the_documented_shape()
    {
        IReadOnlyList<TrackedMeeting> tracked =
        [
            new TrackedMeeting { Meeting = Meeting, Body = "Board of Commissioners", Date = new DateOnly(2026, 10, 15), MeetingPageUri = new Uri("https://guilford.legistar.com/MeetingDetail.aspx?LEGID=1599&GID=590") },
            new TrackedMeeting { Meeting = Meeting with { MeetingId = "1600" }, Body = "Board of Commissioners", Date = new DateOnly(2026, 11, 5) },
        ];

        await State().PutTrackedAsync("guilford", tracked, Ct);
        var read = await State().GetTrackedAsync("guilford", Ct);

        read.ShouldBe(tracked);
        _bucket.Objects[TrackedKey].ContentType.ShouldBe("application/json");
        _bucket.Objects[TrackedKey].Text.ShouldBe(
            "{\"schema_version\":1,\"meetings\":["
            + "{\"platform\":\"legistar\",\"client\":\"guilford\",\"meeting_id\":\"1599\",\"body\":\"Board of Commissioners\",\"date\":\"2026-10-15\","
            + "\"meeting_page_url\":\"https://guilford.legistar.com/MeetingDetail.aspx?LEGID=1599\\u0026GID=590\"},"
            + "{\"platform\":\"legistar\",\"client\":\"guilford\",\"meeting_id\":\"1600\",\"body\":\"Board of Commissioners\",\"date\":\"2026-11-05\",\"meeting_page_url\":null}]}");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"schema_version\":2,\"meetings\":[]}")]
    [InlineData("{\"schema_version\":1}")]
    [InlineData("{\"schema_version\":1,\"meetings\":[{\"platform\":\"escribe\",\"client\":\"g\",\"meeting_id\":\"1\",\"body\":\"B\",\"date\":\"2026-11-05\",\"meeting_page_url\":null}]}")]
    [InlineData("{\"schema_version\":1,\"meetings\":[{\"platform\":\"legistar\",\"client\":\"g\",\"meeting_id\":\"1\",\"body\":\"B\",\"date\":\"2026-11-05\",\"meeting_page_url\":\"not a url\"}]}")]
    [InlineData("{\"schema_version\":1,\"meetings\":[],\"title\":\"x\"}")]
    public async Task Invalid_tracked_meetings_are_a_storage_failure(string json)
    {
        _bucket.Seed(TrackedKey, json);

        var failure = await Should.ThrowAsync<StorageFailedException>(() => State().GetTrackedAsync("guilford", Ct));

        failure.Key.ShouldBe(TrackedKey);
    }

    [Fact]
    public async Task Unsafe_meeting_id_is_rejected_before_any_request()
    {
        await Should.ThrowAsync<ArgumentException>(() => State().GetAsync(Meeting with { MeetingId = "../x" }, Ct));

        _bucket.Operations.ShouldBeEmpty();
    }

    private BucketIngestState State() => new(_bucket);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}

public sealed class BucketRunLogTests
{
    [Fact]
    public async Task Run_is_filed_by_utc_month_and_jurisdiction_in_the_documented_shape()
    {
        var bucket = new InMemoryObjectStore();
        var run = new RunRecord
        {
            RunId = "40623358875-1",
            JurisdictionId = "guilford",
            StartedUtc = new DateTimeOffset(2026, 10, 31, 20, 30, 0, TimeSpan.FromHours(-4)),
            FinishedUtc = new DateTimeOffset(2026, 10, 31, 20, 31, 15, TimeSpan.FromHours(-4)),
            StartDelay = TimeSpan.FromSeconds(754),
            Outcome = RunOutcome.Failed,
            Errors = ["source unreachable"],
        };

        await new BucketRunLog(bucket).WriteAsync(run, TestContext.Current.CancellationToken);

        bucket.Objects["runs/2026/11/guilford/40623358875-1.json"].Text.ShouldBe(
            "{\"schema_version\":1,\"run_id\":\"40623358875-1\",\"jurisdiction_id\":\"guilford\","
            + "\"started_utc\":\"2026-11-01T00:30:00+00:00\",\"finished_utc\":\"2026-11-01T00:31:15+00:00\","
            + "\"start_delay_seconds\":754,\"outcome\":\"failed\",\"errors\":[\"source unreachable\"],\"documents\":[]}");
    }

    [Fact]
    public async Task Retrieved_documents_are_the_run_manifest()
    {
        var bucket = new InMemoryObjectStore();
        var started = new DateTimeOffset(2026, 10, 28, 12, 17, 40, TimeSpan.Zero);
        var run = new RunRecord
        {
            RunId = "40623358875-1",
            JurisdictionId = "guilford",
            StartedUtc = started,
            FinishedUtc = started.AddSeconds(20),
            Outcome = RunOutcome.Succeeded,
            Documents =
            [
                new RetrievedDocument
                {
                    Meeting = new MeetingKey(Platform.Legistar, "guilford", "1599"),
                    Uri = new Uri("https://guilford.legistar1.com/guilford/meetings/2026/10/1599_A_Agenda.pdf"),
                    RetrievedAtUtc = new DateTimeOffset(2026, 10, 28, 8, 17, 52, TimeSpan.FromHours(-4)),
                    Sha256 = Sha256Hash.Compute("agenda"u8),
                    MediaType = "application/pdf",
                    Bytes = 123_456,
                    Created = true,
                },
            ],
        };

        await new BucketRunLog(bucket).WriteAsync(run, TestContext.Current.CancellationToken);

        var json = bucket.Objects["runs/2026/10/guilford/40623358875-1.json"].Text;
        json.ShouldContain("\"start_delay_seconds\":null");
        json.ShouldEndWith(
            "\"documents\":[{\"platform\":\"legistar\",\"client\":\"guilford\",\"meeting_id\":\"1599\","
            + "\"url\":\"https://guilford.legistar1.com/guilford/meetings/2026/10/1599_A_Agenda.pdf\","
            + "\"retrieved_utc\":\"2026-10-28T12:17:52+00:00\",\"sha256\":\"" + Sha256Hash.Compute("agenda"u8).Value + "\","
            + "\"media_type\":\"application/pdf\",\"bytes\":123456,\"created\":true}]}");
    }
}
