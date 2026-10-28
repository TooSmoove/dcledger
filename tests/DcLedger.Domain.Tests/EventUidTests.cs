using Shouldly;
using Xunit;

namespace DcLedger.Domain.Tests;

public sealed class EventUidTests
{
    [Fact]
    public void Uid_is_a_stable_hash_of_jurisdiction_platform_and_meeting_id()
    {
        // Golden value: sha256("guilford\nlegistar\n1599"). Changing it breaks every calendar subscription.
        EventUid.Create("guilford", Platform.Legistar, "1599")
            .ShouldBe("4ed043ba02406ef1d46ab18dadedf77a75447708752e4fea32434db1d40fbcb0@dcledger");
    }

    [Fact]
    public void Different_meetings_get_different_uids()
    {
        var uid = EventUid.Create("guilford", Platform.Legistar, "1599");

        EventUid.Create("guilford", Platform.Legistar, "1600").ShouldNotBe(uid);
        EventUid.Create("winston-salem", Platform.Legistar, "1599").ShouldNotBe(uid);
        EventUid.Create("guilford", Platform.Manual, "1599").ShouldNotBe(uid);
    }

    [Theory]
    [InlineData("", "1599")]
    [InlineData("guilford", " ")]
    public void Blank_parts_are_rejected(string jurisdictionId, string meetingId)
    {
        Should.Throw<ArgumentException>(() => EventUid.Create(jurisdictionId, Platform.Legistar, meetingId));
    }

    [Fact]
    public void Ledger_event_exposes_its_uid()
    {
        var ledgerEvent = new LedgerEvent
        {
            Id = "guilford-boc-2026-11-05",
            JurisdictionId = "guilford",
            Platform = Platform.Legistar,
            PlatformMeetingId = "1599",
            Body = "Board of Commissioners",
            Title = "Regular meeting",
            StartsAt = new DateTimeOffset(2026, 11, 5, 17, 30, 0, TimeSpan.FromHours(-5)),
            Status = EventStatus.Scheduled,
            Sequence = 0,
            Label = EvidenceLabel.ConfirmedOfficial,
            DocumentIds = [],
        };

        ledgerEvent.Uid.ShouldBe(EventUid.Create("guilford", Platform.Legistar, "1599"));
    }
}
