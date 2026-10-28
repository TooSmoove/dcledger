using DcLedger.Domain;

namespace DcLedger.Application.Tests;

/// <summary>Review candidates shaped like the recorded Guilford hearing (tests/fixtures/legistar/guilford, meeting 1599).</summary>
internal static class ReviewCandidates
{
    private static readonly string[] DataCenterRule = ["data_center"];

    public static readonly DateTimeOffset Retrieved = new(2026, 10, 28, 12, 47, 3, TimeSpan.Zero);

    public static ReviewCandidate Hearing() => new()
    {
        JurisdictionId = "guilford",
        JurisdictionName = "Guilford County",
        Meeting = new MeetingKey(Platform.Legistar, "guilford", "1599"),
        Body = "Board of Commissioners",
        Date = new DateOnly(2026, 10, 15),
        Time = new TimeOnly(17, 30),
        StartsAt = new DateTimeOffset(2026, 10, 15, 17, 30, 0, TimeSpan.FromHours(-4)),
        MeetingPageUri = new Uri("https://guilford.legistar.com/MeetingDetail.aspx?LEGID=1599&GID=590"),
        Agenda = new AgendaDocument(
            Sha256Hash.Compute("agenda"u8),
            new Uri("https://guilford.legistar1.com/guilford/meetings/2026/10/1599_A_Board_of_Commissioners_26-10-15_Meeting_Agenda.pdf"),
            "application/pdf",
            123_456,
            Retrieved),
        Items = [new ReviewItem("A.", "2026-439", "Public Hearing", DataCenterRule)],
        FirstSeenUtc = Retrieved,
    };

    /// <summary>Everything that triggers a warning, plus hostile text in every source field.</summary>
    public static ReviewCandidate Troubled() => Hearing() with
    {
        Meeting = new MeetingKey(Platform.Legistar, "guilford", "1631"),
        MeetingPageUri = new Uri("https://guilford.legistar.com/MeetingDetail.aspx?LEGID=1631&GID=590"),
        Time = null,
        StartsAt = null,
        PossiblyCancelled = true,
        SameSlotMeetingIds = ["1599"],
        Items =
        [
            new ReviewItem("@octocat <img src=x> | **A.**", "Z-26/01 @team", "Public Hearing\n# heading", DataCenterRule),
            new ReviewItem(null, "2026-440", null, DataCenterRule),
        ],
    };
}
