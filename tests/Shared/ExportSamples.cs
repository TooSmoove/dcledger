// A small fictional ledger for export tests, compiled into the Application and Publishing test projects (linked from
// their project files). tests/fixtures/ledger/export/ holds the same records as YAML for the CLI tests.
using System.Text;
using DcLedger.Application;
using DcLedger.Domain;

namespace DcLedger.Testing;

/// <summary>
/// Two jurisdictions (one automated, one manual and stale), four documents and three events: a cancelled one on
/// Oct 15, 2026 (EDT) with a title full of CSV and iCalendar specials, one on Nov 3 with non-ASCII text, and the
/// Nov 5, 2026 decision (EST), so every export crosses the Nov 1 daylight-saving change (SPEC AC3).
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
        LastManualCheckUtc = Utc(2026, 11, 2, 9, 0, 0), // 77 h before GeneratedUtc: stale
    };

    public static readonly Document CountyAgenda1599 = Doc("fixture county agenda 1599", "https://fixture-county.example.org/agendas/1599.pdf", Utc(2026, 10, 10, 12, 0, 0), "application/pdf", 123456, SourceType.Official);

    public static readonly Document NewsStory = Doc("fixture news story", "https://news.example.org/2026/10/fixture-county-hearing", Utc(2026, 10, 11, 9, 30, 0), "text/html", 54321, SourceType.News);

    public static readonly Document CountyAgenda1600 = Doc("fixture county agenda 1600", "https://fixture-county.example.org/agendas/1600.pdf", Utc(2026, 10, 28, 12, 47, 3), "application/pdf", 98765, SourceType.Official);

    public static readonly Document CityAgenda = Doc("fixture city agenda 2026-11-03", "https://fixture-city.example.org/council/2026-11-03-agenda.pdf", Utc(2026, 10, 30, 14, 5, 0), "application/pdf", 4567, SourceType.Official);

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
    };

    /// <summary>The ledger as the YAML reader would load it, records deliberately out of order.</summary>
    public static LedgerSnapshot Snapshot() => new()
    {
        Jurisdictions = [Loaded("jurisdictions", County.Id, County), Loaded("jurisdictions", City.Id, City)],
        Documents =
        [
            Loaded("documents", CityAgenda.Id.Value, CityAgenda),
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
        RecordFiles =
        [
            $"jurisdictions/{County.Id}.yaml",
            $"jurisdictions/{City.Id}.yaml",
            $"documents/{CityAgenda.Id.Value}.yaml",
            $"documents/{CountyAgenda1599.Id.Value}.yaml",
            $"documents/{CountyAgenda1600.Id.Value}.yaml",
            $"documents/{NewsStory.Id.Value}.yaml",
            $"events/{November5.Id}.yaml",
            $"events/{October15.Id}.yaml",
            $"events/{November3.Id}.yaml",
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
