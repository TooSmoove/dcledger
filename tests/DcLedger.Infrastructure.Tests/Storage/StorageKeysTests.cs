using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Infrastructure.Storage;
using Shouldly;
using Xunit;

namespace DcLedger.Infrastructure.Tests.Storage;

public sealed class StorageKeysTests
{
    [Fact]
    public void Documents_are_filed_by_the_first_two_hex_digits_of_their_hash()
    {
        var hash = Sha256Hash.Compute("agenda"u8);

        StorageKeys.Document(hash).ShouldBe($"docs/sha256/{hash.Value[..2]}/{hash.Value}");
    }

    [Fact]
    public void Empty_hash_is_rejected()
    {
        Should.Throw<ArgumentException>(() => StorageKeys.Document(default));
    }

    [Fact]
    public void Meeting_state_is_filed_by_platform_client_and_meeting()
    {
        StorageKeys.MeetingState(new MeetingKey(Platform.Legistar, "guilford", "1599"))
            .ShouldBe("state/legistar/guilford/1599.json");
    }

    [Fact]
    public void Runs_are_filed_by_their_utc_start_month_and_jurisdiction()
    {
        var startedEastern = new DateTimeOffset(2026, 10, 31, 20, 30, 0, TimeSpan.FromHours(-4)); // Nov 1, 00:30 UTC

        StorageKeys.Run(startedEastern, "guilford", "40623358875-1").ShouldBe("runs/2026/11/guilford/40623358875-1.json");
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../guilford")]
    [InlineData("guilford/1599")]
    [InlineData(".hidden")]
    [InlineData("two words")]
    [InlineData("line\n")]
    [InlineData("%2e%2e")]
    [InlineData("guilford\\1599")]
    public void Unsafe_segments_are_rejected(string segment)
    {
        Should.Throw<ArgumentException>(() => StorageKeys.MeetingState(new MeetingKey(Platform.Legistar, segment, "1599")));
        Should.Throw<ArgumentException>(() => StorageKeys.MeetingState(new MeetingKey(Platform.Legistar, "guilford", segment)));
        Should.Throw<ArgumentException>(() => StorageKeys.Run(DateTimeOffset.UnixEpoch, "guilford", segment));
        Should.Throw<ArgumentException>(() => StorageKeys.Run(DateTimeOffset.UnixEpoch, segment, "40623358875-1"));
    }

    [Fact]
    public void Overlong_segment_is_rejected()
    {
        var tooLong = new string('a', 129);

        Should.Throw<ArgumentException>(() => StorageKeys.Run(DateTimeOffset.UnixEpoch, "guilford", tooLong));
    }
}
