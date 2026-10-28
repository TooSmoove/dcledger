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
    public async Task Run_is_filed_by_utc_month_in_the_documented_shape()
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

        bucket.Objects["runs/2026/11/40623358875-1.json"].Text.ShouldBe(
            "{\"schema_version\":1,\"run_id\":\"40623358875-1\",\"jurisdiction_id\":\"guilford\","
            + "\"started_utc\":\"2026-11-01T00:30:00+00:00\",\"finished_utc\":\"2026-11-01T00:31:15+00:00\","
            + "\"start_delay_seconds\":754,\"outcome\":\"failed\",\"errors\":[\"source unreachable\"]}");
    }
}
