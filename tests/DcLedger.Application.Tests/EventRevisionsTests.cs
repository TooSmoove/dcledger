using DcLedger.Domain;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

public sealed class EventRevisionsTests
{
    private const string EventFile = "events/fixture-boc-2026-11-05.yaml";
    private static readonly Sha256Hash Agenda = Sha256Hash.Compute("agenda"u8);
    private static readonly Sha256Hash Minutes = Sha256Hash.Compute("minutes"u8);

    [Fact]
    public void Unchanged_event_is_fine()
    {
        Check(Published(), Published()).ShouldBeEmpty();
    }

    [Fact]
    public void Cancellation_with_a_raised_sequence_is_fine()
    {
        Check(Published(), Published() with { Status = EventStatus.Cancelled, Sequence = 1 }).ShouldBeEmpty();
    }

    [Fact]
    public void Cancellation_without_a_raised_sequence_is_rejected()
    {
        var problem = Check(Published(), Published() with { Status = EventStatus.Cancelled }).ShouldHaveSingleItem();

        problem.Rule.ShouldBe(ValidationRule.SequenceNotRaised);
        problem.File.ShouldBe(EventFile);
        problem.RecordId.ShouldBe("fixture-boc-2026-11-05");
        problem.Message.ShouldBe("status changed but sequence is still 0. Raise it to 1 so calendar apps apply the change.");
    }

    [Fact]
    public void Every_changed_field_is_named_in_yaml_terms()
    {
        var moved = Published() with
        {
            Body = "Board of Commissioners Work Session",
            Title = "Fixture: work session",
            CaseNumber = "2026-452",
            StartsAt = new DateTimeOffset(2026, 11, 12, 17, 30, 0, TimeSpan.FromHours(-5)),
            Status = EventStatus.Cancelled,
            Label = EvidenceLabel.ReportedNews,
            DocumentIds = [Agenda, Minutes],
            Location = new Location { Latitude = 36.0726, Longitude = -79.792 },
        };

        EventRevisions.ChangedFields(Published(), moved)
            .ShouldBe(new List<string> { "body", "title", "case_number", "starts_at", "status", "label", "document_ids", "location" });
    }

    [Fact]
    public void Same_instant_written_with_the_same_offset_is_no_change()
    {
        var again = Published() with { StartsAt = new DateTimeOffset(2026, 11, 5, 17, 30, 0, TimeSpan.FromHours(-5)) };

        EventRevisions.ChangedFields(Published(), again).ShouldBeEmpty();
    }

    [Fact]
    public void First_seen_time_is_not_a_published_change()
    {
        Check(Published(), Published() with { FirstSeenUtc = new DateTimeOffset(2026, 10, 30, 22, 24, 55, TimeSpan.Zero) }).ShouldBeEmpty();
    }

    [Fact]
    public void Sequence_may_go_up_without_other_changes()
    {
        Check(Published(), Published() with { Sequence = 2 }).ShouldBeEmpty();
    }

    [Fact]
    public void Sequence_never_goes_down()
    {
        var problem = Check(Published() with { Sequence = 2 }, Published() with { Sequence = 1, Title = "Changed" }).ShouldHaveSingleItem();

        problem.Rule.ShouldBe(ValidationRule.SequenceNotRaised);
        problem.Message.ShouldBe("sequence went down from 2 to 1; it only goes up.");
    }

    [Theory]
    [InlineData("platform_meeting_id")]
    [InlineData("jurisdiction_id")]
    [InlineData("platform")]
    public void Changing_what_the_uid_is_built_from_is_rejected_even_with_a_raised_sequence(string field)
    {
        var now = field switch
        {
            "platform_meeting_id" => Published() with { PlatformMeetingId = "1631", Sequence = 1 },
            "jurisdiction_id" => Published() with { JurisdictionId = "other", Sequence = 1 },
            _ => Published() with { Platform = Platform.Manual, Sequence = 1 },
        };

        Check(Published(), now).ShouldHaveSingleItem().Rule.ShouldBe(ValidationRule.UidChanged);
    }

    [Fact]
    public void New_and_removed_events_are_left_to_other_rules()
    {
        var other = Published() with { Id = "fixture-boc-2026-11-19" };

        EventRevisions.Check([new(EventFile, Published())], [new("events/fixture-boc-2026-11-19.yaml", other)]).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_ledger_reports_revisions_only_when_given_the_previous_data()
    {
        var previous = new LedgerSnapshot { Events = [new(EventFile, Published())] };
        var current = new LedgerSnapshot { Events = [new(EventFile, Published() with { Title = "Changed" })] };

        ValidateLedger.Check(current, [], previous).Errors.ShouldContain(error => error.Rule == ValidationRule.SequenceNotRaised);
        ValidateLedger.Check(current, []).Errors.ShouldNotContain(error => error.Rule == ValidationRule.SequenceNotRaised);
    }

    private static IReadOnlyList<ValidationError> Check(LedgerEvent was, LedgerEvent now) =>
        EventRevisions.Check([new(EventFile, now)], [new(EventFile, was)]);

    private static LedgerEvent Published() => new()
    {
        Id = "fixture-boc-2026-11-05",
        JurisdictionId = "fixture",
        Platform = Platform.Legistar,
        PlatformMeetingId = "1600",
        Body = "Board of Commissioners",
        Title = "Fixture: regular meeting",
        StartsAt = new DateTimeOffset(2026, 11, 5, 17, 30, 0, TimeSpan.FromHours(-5)),
        Status = EventStatus.Scheduled,
        Sequence = 0,
        Label = EvidenceLabel.ConfirmedOfficial,
        DocumentIds = [Agenda],
    };
}
