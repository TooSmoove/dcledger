// A small fictional ledger for export tests, compiled into the Application and Publishing test projects (linked from
// their project files). tests/fixtures/ledger/export/ holds the same records as YAML for the CLI tests.
using System.Text;
using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Testing;

/// <summary>
/// Two jurisdictions (one automated, one manual and stale), four documents and three events: a cancelled one on
/// Oct 15, 2026 (EDT) with a title full of CSV and iCalendar specials, one on Nov 3 with non-ASCII text, and the
/// Nov 5, 2026 decision (EST), so every export crosses the Nov 1 daylight-saving change (SPEC AC3). Only the Nov 5
/// item has a location, so exports show both a point and its absence. One action records the Nov 5 decision, backed by
/// a document that no event lists. The city's agenda was saved by a person (<see cref="Retrieval.Manual"/>, issue #113);
/// every other document was fetched.
/// </summary>
internal static class ExportSamples
{
    public static readonly DateTimeOffset GeneratedUtc = Utc(2026, 11, 5, 14, 47, 0);

    public static readonly Jurisdiction County = new()
    {
        Id = "fixture-county",
        Name = "Fixture County",
        Platform = Platform.Legistar,
        LegistarClient = "fixture",
        Bodies = ["Board of Commissioners"],
    };

    public static readonly Jurisdiction City = new()
    {
        Id = "fixture-city",
        Name = "Fixture City",
        Platform = Platform.Manual,
        LastManualCheckUtc = Utc(2026, 10, 27, 9, 0, 0), // 9 days before GeneratedUtc: stale even at the manual 7 days
    };

    public static readonly Document CountyAgenda1599 = Doc("fixture county agenda 1599", "https://fixture-county.example.org/agendas/1599.pdf", Utc(2026, 10, 10, 12, 0, 0), "application/pdf", 123456, SourceType.Official);

    public static readonly Document NewsStory = Doc("fixture news story", "https://news.example.org/2026/10/fixture-county-hearing", Utc(2026, 10, 11, 9, 30, 0), "text/html", 54321, SourceType.News);

    public static readonly Document CountyAgenda1600 = Doc("fixture county agenda 1600", "https://fixture-county.example.org/agendas/1600.pdf", Utc(2026, 10, 28, 12, 47, 3), "application/pdf", 98765, SourceType.Official);

    public static readonly Document CityAgenda = Doc("fixture city agenda 2026-11-03", "https://fixture-city.example.org/council/2026-11-03-agenda.pdf", Utc(2026, 10, 30, 14, 5, 0), "application/pdf", 4567, SourceType.Official) with { Retrieval = Retrieval.Manual };

    public static readonly Document CountyActionDetail1600 = Doc("fixture county action detail 1600", "https://fixture-county.example.org/meetings/1600/actions/7", Utc(2026, 11, 6, 13, 0, 0), "text/html", 8765, SourceType.Official);

    /// <summary>Oct 15, 2026 5:30 PM EDT (21:30Z); cancelled, so SEQUENCE is 1. News story listed first, on purpose.</summary>
    public static readonly LedgerEvent October15 = new()
    {
        Id = "fixture-county-boc-2026-10-15",
        JurisdictionId = County.Id,
        Platform = Platform.Legistar,
        PlatformMeetingId = "1599",
        Body = "Board of Commissioners",
        Title = "Rezoning hearing, \"Project Example\"; phase 2",
        CaseNumber = "Z-26-01",
        StartsAt = new DateTimeOffset(2026, 10, 15, 17, 30, 0, TimeSpan.FromHours(-4)),
        Status = EventStatus.Cancelled,
        Sequence = 1,
        Label = EvidenceLabel.ConfirmedOfficial,
        DocumentIds = [NewsStory.Id, CountyAgenda1599.Id],
        FirstSeenUtc = Utc(2026, 10, 10, 12, 0, 0),
    };

    /// <summary>Nov 3, 2026 6:00 PM EST, entered by hand.</summary>
    public static readonly LedgerEvent November3 = new()
    {
        Id = "fixture-city-council-2026-11-03",
        JurisdictionId = City.Id,
        Platform = Platform.Manual,
        PlatformMeetingId = "council-2026-11-03",
        Body = "City Council",
        Title = "Café district text amendment: data centers",
        StartsAt = new DateTimeOffset(2026, 11, 3, 18, 0, 0, TimeSpan.FromHours(-5)),
        Status = EventStatus.Scheduled,
        Sequence = 0,
        Label = EvidenceLabel.ConfirmedOfficial,
        DocumentIds = [CityAgenda.Id],
    };

    /// <summary>Nov 5, 2026 5:30 PM EST (22:30Z).</summary>
    public static readonly LedgerEvent November5 = new()
    {
        Id = "fixture-county-boc-2026-11-05",
        JurisdictionId = County.Id,
        Platform = Platform.Legistar,
        PlatformMeetingId = "1600",
        Body = "Board of Commissioners",
        Title = "Data center moratorium decision",
        StartsAt = new DateTimeOffset(2026, 11, 5, 17, 30, 0, TimeSpan.FromHours(-5)),
        Status = EventStatus.Scheduled,
        Sequence = 0,
        Label = EvidenceLabel.ConfirmedOfficial,
        DocumentIds = [CountyAgenda1600.Id],
        FirstSeenUtc = Utc(2026, 10, 28, 12, 47, 3),
        Location = new Location { Latitude = 36.0726, Longitude = -79.792 },
    };

    /// <summary>The Nov 5 decision: adopted as amended, 8-1, confirmed by the platform's action detail.</summary>
    public static readonly LocalAction November5Moratorium = new()
    {
        Id = "fixture-county-moratorium-2026-11-05",
        JurisdictionId = County.Id,
        EventId = November5.Id,
        ActionType = ActionType.Moratorium,
        Outcome = ActionOutcome.AdoptedAmended,
        DecidedOn = new DateOnly(2026, 11, 5),
        Votes = "8-1",
        Label = EvidenceLabel.ConfirmedOfficial,
        DocumentIds = [CountyActionDetail1600.Id],
    };

    /// <summary>
    /// Every decision term (issue #115), with each text term starting with a CSV formula trigger and one holding markup,
    /// so the exporters' guard and escaping show. Not on <see cref="November5Moratorium"/>: tests add it where needed.
    /// </summary>
    public static readonly DecisionTerms FullTerms = new()
    {
        InstrumentNumber = "=26-01",
        Threshold = "+electrical power demand exceeding 10 megawatts",
        Scope = "-development approvals for <data center> facilities & accessory uses",
        EffectiveOn = new DateOnly(2026, 11, 5),
        ExpiresOn = new DateOnly(2027, 5, 4),
        EarlyTermination = "@upon adoption of text amendments, whichever occurs first",
        Exemptions = "'applications deemed complete before the effective date",
        SuccessorId = "fixture-county-moratorium-2027-05-04",
        Status = ActionStatus.Superseded,
    };

    /// <summary>The model with <see cref="FullTerms"/> on the Nov 5 decision.</summary>
    public static ExportModel ModelWithTerms()
    {
        var model = Model();
        return model with { Actions = [.. model.Actions.Select(item => item with { Action = item.Action with { Terms = FullTerms } })] };
    }

    /// <summary>The date the Nov 5 decision's document gives for the next step (ADR-0010); not on the default sample.</summary>
    public static readonly DateOnly NextOn = new(2026, 12, 3);

    /// <summary>The model with <see cref="NextOn"/> on the Nov 5 decision.</summary>
    public static ExportModel ModelWithNextOn()
    {
        var model = Model();
        return model with { Actions = [.. model.Actions.Select(item => item with { Action = item.Action with { NextOn = NextOn } })] };
    }

    /// <summary>A correction to the Nov 5 decision's vote, with a linked issue (issue #117).</summary>
    public static readonly Correction VoteCorrection = new()
    {
        Id = "2026-11-24-fixture-vote-count",
        ReportedOn = new DateOnly(2026, 11, 23),
        CorrectedOn = new DateOnly(2026, 11, 24),
        RecordIds = [November5Moratorium.Id, County.Id],
        Summary = "The vote was recorded as \"9-0\"; the <minutes> show 8-1 & one absence.",
        Report = 131,
    };

    /// <summary>A correction to page wording only: no record, no issue, corrected the day it was reported.</summary>
    public static readonly Correction WordingCorrection = new()
    {
        Id = "2026-11-22-methodology-interval",
        ReportedOn = new DateOnly(2026, 11, 22),
        CorrectedOn = new DateOnly(2026, 11, 22),
        Summary = "The methodology page gave the publish interval as 12 hours; it is 6 hours.",
    };

    /// <summary>The model with both corrections, loaded newest first so the builder's ordering shows.</summary>
    public static ExportModel ModelWithCorrections() =>
        ExportModelBuilder.Build(
            Snapshot() with
            {
                Corrections =
                [
                    Loaded("corrections", VoteCorrection.Id, VoteCorrection),
                    Loaded("corrections", WordingCorrection.Id, WordingCorrection),
                ],
            },
            Status()).Model;

    /// <summary>The smallest bytes the reader accepts as a WebP picture (issue #132).</summary>
    public static readonly byte[] ThumbnailBytes = [.. "RIFF"u8, 4, 0, 0, 0, .. "WEBP"u8];

    /// <summary>
    /// The model with a reviewed first-page picture of the Nov 5 action detail page (issue #132), plus a picture of a
    /// document no item lists, which the export leaves out.
    /// </summary>
    public static ExportModel ModelWithThumbnail()
    {
        var thumbnail = new Thumbnail { Sha256 = Sha256Hash.Compute(ThumbnailBytes), Width = 240, Height = 311, Reviewed = true };
        var detail = CountyActionDetail1600 with { Kind = DocumentKind.Legislation, Title = "Item 2026-1600", Thumbnail = thumbnail };
        var unlistedSource = Doc("fixture unlisted minutes", "https://fixture-county.example.org/minutes/1590.pdf", Utc(2026, 10, 1, 12, 0, 0), "application/pdf", 2345, SourceType.Official);
        var unlisted = unlistedSource with { Kind = DocumentKind.Minutes, Thumbnail = thumbnail };
        var snapshot = Snapshot();
        snapshot = snapshot with
        {
            Documents =
            [
                .. snapshot.Documents.Where(loaded => loaded.Record.Id != detail.Id),
                Loaded("documents", detail.Id.Value, detail),
                Loaded("documents", unlisted.Id.Value, unlisted),
            ],
            Thumbnails =
            [
                new ThumbnailFile($"thumbnails/{unlisted.Id.Value}.webp", unlisted.Id, thumbnail.Sha256, ThumbnailBytes),
                new ThumbnailFile($"thumbnails/{detail.Id.Value}.webp", detail.Id, thumbnail.Sha256, ThumbnailBytes),
            ],
        };
        return ExportModelBuilder.Build(snapshot, Status()).Model;
    }

    /// <summary>The ledger as the YAML reader would load it, records deliberately out of order.</summary>
    public static LedgerSnapshot Snapshot() => new()
    {
        Jurisdictions = [Loaded("jurisdictions", County.Id, County), Loaded("jurisdictions", City.Id, City)],
        Documents =
        [
            Loaded("documents", CityAgenda.Id.Value, CityAgenda),
            Loaded("documents", CountyActionDetail1600.Id.Value, CountyActionDetail1600),
            Loaded("documents", CountyAgenda1599.Id.Value, CountyAgenda1599),
            Loaded("documents", CountyAgenda1600.Id.Value, CountyAgenda1600),
            Loaded("documents", NewsStory.Id.Value, NewsStory),
        ],
        Events =
        [
            Loaded("events", November5.Id, November5),
            Loaded("events", October15.Id, October15),
            Loaded("events", November3.Id, November3),
        ],
        Actions = [Loaded("actions", November5Moratorium.Id, November5Moratorium)],
        RecordFiles =
        [
            $"jurisdictions/{County.Id}.yaml",
            $"jurisdictions/{City.Id}.yaml",
            $"documents/{CityAgenda.Id.Value}.yaml",
            $"documents/{CountyActionDetail1600.Id.Value}.yaml",
            $"documents/{CountyAgenda1599.Id.Value}.yaml",
            $"documents/{CountyAgenda1600.Id.Value}.yaml",
            $"documents/{NewsStory.Id.Value}.yaml",
            $"events/{November5.Id}.yaml",
            $"events/{October15.Id}.yaml",
            $"events/{November3.Id}.yaml",
            $"actions/{November5Moratorium.Id}.yaml",
        ],
    };

    /// <summary>The county checked 2.5 hours before the status was built.</summary>
    public static IngestStatus Status() => new()
    {
        GeneratedUtc = GeneratedUtc,
        LastCheckedUtc = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal) { [County.Id] = Utc(2026, 11, 5, 12, 17, 43) },
    };

    /// <summary>The model the exporters see.</summary>
    public static ExportModel Model() => ExportModelBuilder.Build(Snapshot(), Status()).Model;

    private static LoadedRecord<T> Loaded<T>(string folder, string id, T record) => new($"{folder}/{id}.yaml", record);

    private static Document Doc(string seed, string url, DateTimeOffset retrieved, string mediaType, long bytes, SourceType sourceType) => new()
    {
        Id = Sha256Hash.Compute(Encoding.UTF8.GetBytes(seed)),
        Url = new Uri(url),
        RetrievedAtUtc = retrieved,
        MediaType = mediaType,
        Bytes = bytes,
        SourceType = sourceType,
    };

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);
}
