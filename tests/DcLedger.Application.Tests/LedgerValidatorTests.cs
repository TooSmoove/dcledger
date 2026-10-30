using DcLedger.Domain;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

public sealed class LedgerValidatorTests
{
    private static readonly Sha256Hash OfficialDoc = Sha256Hash.Compute("official"u8);
    private static readonly Sha256Hash NewsDoc = Sha256Hash.Compute("news"u8);

    [Fact]
    public void Valid_snapshot_has_no_problems()
    {
        Validate(Snapshot()).ShouldBeEmpty();
    }

    [Fact]
    public void Load_errors_are_reported_first()
    {
        var loadError = new ValidationError("events/x.yaml", "x", ValidationRule.UnknownField, "'color' isn't part of this record type.");

        Validate(Snapshot() with { LoadErrors = [loadError] }).ShouldHaveSingleItem().ShouldBe(loadError);
    }

    [Fact]
    public void Id_must_match_the_file_name()
    {
        var snapshot = Snapshot(ledgerEvent: Event() with { Id = "renamed" });

        Rule(snapshot).ShouldBe(ValidationRule.IdMismatch);
    }

    [Fact]
    public void Unknown_jurisdiction_is_reported()
    {
        Rule(Snapshot(ledgerEvent: Event() with { JurisdictionId = "nowhere" })).ShouldBe(ValidationRule.FkMissing);
    }

    [Fact]
    public void Item_without_documents_is_reported()
    {
        Rule(Snapshot(ledgerEvent: Event() with { DocumentIds = [] })).ShouldBe(ValidationRule.DocMissing);
    }

    [Fact]
    public void Reference_to_a_missing_document_is_reported()
    {
        var missing = Sha256Hash.Compute("missing"u8);

        Rule(Snapshot(ledgerEvent: Event() with { DocumentIds = [OfficialDoc, missing] })).ShouldBe(ValidationRule.DocMissing);
    }

    [Fact]
    public void Confirmed_label_needs_an_official_document()
    {
        var newsOnly = Event() with { DocumentIds = [NewsDoc], Label = EvidenceLabel.ConfirmedOfficial };

        Rule(Snapshot(ledgerEvent: newsOnly)).ShouldBe(ValidationRule.LabelInvalid);
    }

    [Fact]
    public void Reported_label_with_a_news_document_is_valid()
    {
        var reported = Event() with { DocumentIds = [NewsDoc], Label = EvidenceLabel.ReportedNews };

        Validate(Snapshot(ledgerEvent: reported)).ShouldBeEmpty();
    }

    [Fact]
    public void Daylight_offset_in_november_is_not_eastern()
    {
        var wrong = Event() with { StartsAt = new DateTimeOffset(2026, 11, 5, 18, 30, 0, TimeSpan.FromHours(-4)) };

        Rule(Snapshot(ledgerEvent: wrong)).ShouldBe(ValidationRule.OffsetNotEastern);
    }

    [Fact]
    public void Wall_time_repeated_when_daylight_saving_ends_is_rejected()
    {
        var repeated = Event() with { StartsAt = new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-4)) };

        Rule(Snapshot(ledgerEvent: repeated)).ShouldBe(ValidationRule.DstGapOrOverlap);
    }

    [Fact]
    public void Legistar_jurisdiction_needs_a_client_and_bodies()
    {
        var jurisdiction = Jurisdiction() with { LegistarClient = null, Bodies = [] };

        Rule(Snapshot(jurisdiction: jurisdiction)).ShouldBe(ValidationRule.InvalidValue);
    }

    [Fact]
    public void Removed_or_renamed_record_is_reported()
    {
        string[] previous = ["events/published-before.yaml"];

        var errors = LedgerValidator.Validate(Snapshot(), previous);

        errors.ShouldHaveSingleItem().Rule.ShouldBe(ValidationRule.SlugChanged);
        errors[0].File.ShouldBe("events/published-before.yaml");
    }

    [Fact]
    public void Record_that_still_exists_is_not_reported_as_changed()
    {
        string[] previous = ["events/fixture-boc-2026-11-05.yaml"];

        LedgerValidator.Validate(Snapshot(), previous).ShouldBeEmpty();
    }

    [Fact]
    public void A_location_inside_north_carolina_is_valid()
    {
        var located = Event() with { Location = new Location { Latitude = 36.0726, Longitude = -79.792 } };

        Validate(Snapshot(ledgerEvent: located)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(-79.792, 36.0726)] // lat and lon swapped
    [InlineData(36.0726, 79.792)] // minus sign missing
    [InlineData(37.0, -79.792)] // Virginia
    [InlineData(33.8, -79.0)] // South Carolina
    public void A_location_outside_north_carolina_is_reported(double latitude, double longitude)
    {
        var located = Event() with { Location = new Location { Latitude = latitude, Longitude = longitude } };

        var problem = Validate(Snapshot(ledgerEvent: located)).ShouldHaveSingleItem();
        problem.Rule.ShouldBe(ValidationRule.LocationOutOfRange);
        problem.Message.ShouldContain("outside North Carolina");
    }

    private static IReadOnlyList<ValidationError> Validate(LedgerSnapshot snapshot) => LedgerValidator.Validate(snapshot, []);

    private static string Rule(LedgerSnapshot snapshot) => Validate(snapshot).ShouldHaveSingleItem().Rule;

    private static LedgerSnapshot Snapshot(Jurisdiction? jurisdiction = null, LedgerEvent? ledgerEvent = null)
    {
        const string eventFile = "events/fixture-boc-2026-11-05.yaml";
        return new LedgerSnapshot
        {
            Jurisdictions = [new("jurisdictions/fixture.yaml", jurisdiction ?? Jurisdiction())],
            Documents =
            [
                new($"documents/{OfficialDoc}.yaml", Document(OfficialDoc, SourceType.Official)),
                new($"documents/{NewsDoc}.yaml", Document(NewsDoc, SourceType.News)),
            ],
            Events = [new(eventFile, ledgerEvent ?? Event())],
            RecordFiles = ["jurisdictions/fixture.yaml", eventFile, $"documents/{OfficialDoc}.yaml", $"documents/{NewsDoc}.yaml"],
        };
    }

    private static Jurisdiction Jurisdiction() => new()
    {
        Id = "fixture",
        Name = "Fixture County",
        Platform = Platform.Legistar,
        LegistarClient = "fixture",
        Bodies = ["Board of Commissioners"],
    };

    private static Document Document(Sha256Hash id, SourceType sourceType) => new()
    {
        Id = id,
        Url = new Uri("https://example.org/agenda.pdf"),
        RetrievedAtUtc = new DateTimeOffset(2026, 10, 25, 21, 17, 5, TimeSpan.Zero),
        MediaType = "application/pdf",
        Bytes = 1,
        SourceType = sourceType,
    };

    private static LedgerEvent Event() => new()
    {
        Id = "fixture-boc-2026-11-05",
        JurisdictionId = "fixture",
        Platform = Platform.Legistar,
        PlatformMeetingId = "1599",
        Body = "Board of Commissioners",
        Title = "Fixture: regular meeting",
        StartsAt = new DateTimeOffset(2026, 11, 5, 17, 30, 0, TimeSpan.FromHours(-5)),
        Status = EventStatus.Scheduled,
        Sequence = 0,
        Label = EvidenceLabel.ConfirmedOfficial,
        DocumentIds = [OfficialDoc],
    };
}
