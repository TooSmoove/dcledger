using DcLedger.Domain;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

/// <summary>
/// A jurisdiction's note is a public statement like any other (SPEC AC2): it needs a document that states it, every
/// document it names must be in the ledger, and documents without a note are a mistake.
/// </summary>
public sealed class JurisdictionNoteRulesTests
{
    private static readonly Sha256Hash OrderDoc = Sha256Hash.Compute("order"u8);
    private static readonly Sha256Hash UnknownDoc = Sha256Hash.Compute("unknown"u8);

    [Fact]
    public void A_note_with_a_document_in_the_ledger_is_valid()
    {
        Validate("A court order states it.", [OrderDoc]).ShouldBeEmpty();
    }

    [Fact]
    public void A_jurisdiction_without_a_note_is_valid()
    {
        Validate(null, []).ShouldBeEmpty();
    }

    [Fact]
    public void A_note_without_documents_is_reported()
    {
        var error = Validate("A court order states it.", []).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.DocMissing);
        error.File.ShouldBe("jurisdictions/fixture.yaml");
        error.RecordId.ShouldBe("fixture");
    }

    [Fact]
    public void A_note_document_missing_from_the_ledger_is_reported()
    {
        var error = Validate("A court order states it.", [OrderDoc, UnknownDoc]).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.DocMissing);
        error.Message.ShouldBe($"document '{UnknownDoc}' is missing or failed to load.");
    }

    [Fact]
    public void Note_documents_without_a_note_are_reported()
    {
        var error = Validate(null, [OrderDoc]).ShouldHaveSingleItem();

        error.Rule.ShouldBe(ValidationRule.InvalidValue);
        error.Message.ShouldBe("note_document_ids is only allowed with a note.");
    }

    private static IReadOnlyList<ValidationError> Validate(string? note, IReadOnlyList<Sha256Hash> noteDocumentIds)
    {
        var jurisdiction = new Jurisdiction
        {
            Id = "fixture",
            Name = "Fixture County",
            Platform = Platform.Manual,
            CoverageTier = CoverageTier.Decisions,
            Note = note,
            NoteDocumentIds = noteDocumentIds,
        };
        var snapshot = new LedgerSnapshot
        {
            Jurisdictions = [new("jurisdictions/fixture.yaml", jurisdiction)],
            Documents = [new($"documents/{OrderDoc}.yaml", Document())],
        };
        return LedgerValidator.Validate(snapshot, []);
    }

    private static Document Document() => new()
    {
        Id = OrderDoc,
        Url = new Uri("https://example.org/order.pdf"),
        RetrievedAtUtc = new DateTimeOffset(2026, 11, 17, 20, 0, 0, TimeSpan.Zero),
        MediaType = "application/pdf",
        Bytes = 1,
        SourceType = SourceType.News,
    };
}
