using System.Text;
using DcLedger.Domain;
using DcLedger.Testing;
using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

public sealed class ArchiveDocumentTests
{
    private static readonly Uri ActionPage = new("https://fixture-county.example.org/meetings/1600/actions/7");
    private static readonly byte[] Page = Encoding.UTF8.GetBytes("<html>fixture action detail</html>");

    private readonly FixedTime _time = new(new DateTimeOffset(2026, 11, 6, 13, 0, 0, TimeSpan.Zero).AddTicks(1234567));
    private readonly FakeFetcher _fetcher;
    private readonly MemoryDocumentStore _store = new();

    public ArchiveDocumentTests()
    {
        _fetcher = new FakeFetcher(_time);
        _fetcher.Responses[ActionPage] = (Page, "text/html");
    }

    [Fact]
    public async Task Stores_the_bytes_under_their_hash_and_returns_the_ledger_record()
    {
        var result = await Archive(SourceType.Official);

        result.Succeeded.ShouldBeTrue();
        result.Created.ShouldBeTrue();
        var document = result.Document.ShouldNotBeNull();
        document.Id.ShouldBe(Sha256Hash.Compute(Page));
        document.Url.ShouldBe(ActionPage);
        document.MediaType.ShouldBe("text/html");
        document.Bytes.ShouldBe(Page.Length);
        document.SourceType.ShouldBe(SourceType.Official);
        _store.Documents.Keys.ShouldBe([document.Id]);
    }

    [Fact]
    public async Task Retrieval_time_is_kept_to_the_second_in_utc()
    {
        var document = (await Archive(SourceType.News)).Document.ShouldNotBeNull();

        document.RetrievedAtUtc.ShouldBe(new DateTimeOffset(2026, 11, 6, 13, 0, 0, TimeSpan.Zero));
        document.SourceType.ShouldBe(SourceType.News);
    }

    [Fact]
    public async Task Archiving_the_same_bytes_again_writes_nothing()
    {
        await Archive(SourceType.Official);

        var again = await Archive(SourceType.Official);

        again.Succeeded.ShouldBeTrue();
        again.Created.ShouldBeFalse();
        _store.Writes.ShouldBe(1);
    }

    [Fact]
    public async Task A_dry_run_hashes_but_stores_nothing()
    {
        var result = await new ArchiveDocument(_fetcher, _store).RunAsync(new ArchiveRequest(ActionPage, SourceType.Official, DryRun: true), CancellationToken.None);

        result.Document.ShouldNotBeNull().Id.ShouldBe(Sha256Hash.Compute(Page));
        result.Created.ShouldBeFalse();
        _store.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_media_type_is_recorded_as_octet_stream()
    {
        _fetcher.Responses[ActionPage] = (Page, null);

        (await Archive(SourceType.Official)).Document.ShouldNotBeNull().MediaType.ShouldBe("application/octet-stream");
    }

    [Fact]
    public async Task Plain_http_is_refused_before_anything_is_fetched()
    {
        var result = await new ArchiveDocument(_fetcher, _store).RunAsync(
            new ArchiveRequest(new Uri("http://fixture-county.example.org/a"), SourceType.Official, DryRun: false), CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe("Only https URLs can be archived.");
        _fetcher.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_retrieval_is_a_result_and_stores_nothing()
    {
        var result = await new ArchiveDocument(_fetcher, _store).RunAsync(
            new ArchiveRequest(new Uri("https://fixture-county.example.org/missing"), SourceType.Official, DryRun: false), CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Document.ShouldBeNull();
        result.Error.ShouldNotBeNull().ShouldContain("HttpStatus");
        _store.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bucket_failure_is_a_result_without_a_record()
    {
        _store.Failure = new StorageFailedException("docs/sha256/x", "R2 answered 503.");

        var result = await Archive(SourceType.Official);

        result.Succeeded.ShouldBeFalse();
        result.Document.ShouldBeNull();
        result.Error.ShouldNotBeNull().ShouldContain("R2 answered 503.");
    }

    [Fact]
    public void The_offered_record_is_written_in_the_ledger_s_formats()
    {
        var lines = DocumentRecordYaml.Lines(
            Sha256Hash.Compute(Page), ActionPage, new DateTimeOffset(2026, 11, 6, 13, 0, 0, TimeSpan.Zero), "text/html", 42, SourceType.News);

        lines.ShouldBe(
        [
            $"id: {Sha256Hash.Compute(Page).Value}",
            "url: \"https://fixture-county.example.org/meetings/1600/actions/7\"",
            "retrieved_at: 2026-11-06T13:00:00Z",
            "media_type: \"text/html\"",
            "bytes: 42",
            "source_type: news",
        ]);
        DocumentRecordYaml.PathFor(Sha256Hash.Compute(Page)).ShouldBe($"data/documents/{Sha256Hash.Compute(Page).Value}.yaml");
    }

    [Fact]
    public async Task A_saved_copy_is_stored_without_fetching_and_marked_manual()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 fixture minutes");
        var savedAt = new DateTimeOffset(2026, 11, 9, 22, 40, 12, TimeSpan.Zero).AddTicks(9999);
        var minutes = new Uri("https://fixture-city.example.org/FileStream.ashx?DocumentId=1");

        var result = await new ArchiveDocument(_fetcher, _store).RunAsync(
            new ArchiveRequest(minutes, SourceType.Official, DryRun: false, new SavedCopy(pdf, "minutes.pdf", savedAt)), CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        _fetcher.Requests.ShouldBeEmpty();
        var document = result.Document.ShouldNotBeNull();
        document.Id.ShouldBe(Sha256Hash.Compute(pdf));
        document.Url.ShouldBe(minutes);
        document.RetrievedAtUtc.ShouldBe(new DateTimeOffset(2026, 11, 9, 22, 40, 12, TimeSpan.Zero));
        document.MediaType.ShouldBe("application/pdf");
        document.Bytes.ShouldBe(pdf.Length);
        document.Retrieval.ShouldBe(Retrieval.Manual);
        _store.Documents.Keys.ShouldBe([document.Id]);
    }

    [Fact]
    public async Task A_fetched_document_is_marked_fetched()
    {
        (await Archive(SourceType.Official)).Document.ShouldNotBeNull().Retrieval.ShouldBe(Retrieval.Fetched);
    }

    [Fact]
    public async Task An_empty_saved_copy_is_refused()
    {
        var result = await new ArchiveDocument(_fetcher, _store).RunAsync(
            new ArchiveRequest(ActionPage, SourceType.Official, DryRun: false, new SavedCopy(Array.Empty<byte>(), "minutes.pdf", _time.GetUtcNow())),
            CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe("The file is empty.");
        _store.Documents.ShouldBeEmpty();
    }

    [Fact]
    public void A_manual_record_says_so_and_a_fetched_one_leaves_the_key_out()
    {
        var id = Sha256Hash.Compute(Page);
        var at = new DateTimeOffset(2026, 11, 6, 13, 0, 0, TimeSpan.Zero);

        var manual = DocumentRecordYaml.Lines(id, ActionPage, at, "application/pdf", 42, SourceType.Official, Retrieval.Manual);
        var fetched = DocumentRecordYaml.Lines(id, ActionPage, at, "application/pdf", 42, SourceType.Official, Retrieval.Fetched);

        manual[manual.Count - 1].ShouldBe("retrieval: manual");
        fetched.Count.ShouldBe(manual.Count - 1);
        fetched[fetched.Count - 1].ShouldBe("source_type: official");
    }

    [Theory]
    [InlineData("%PDF-1.4 rest", "saved.bin", "application/pdf")]
    [InlineData("%PDF-1.4 rest", "minutes.html", "application/pdf")]
    [InlineData("<html></html>", "minutes.HTML", "text/html")]
    [InlineData("<html></html>", "minutes.htm", "text/html")]
    [InlineData("plain words", "minutes.txt", "text/plain")]
    [InlineData("not really a pdf", "minutes.pdf", "application/octet-stream")]
    [InlineData("anything", "minutes", "application/octet-stream")]
    public void A_saved_file_s_media_type_comes_from_its_bytes_then_its_extension(string content, string fileName, string expected)
    {
        MediaTypeGuess.Of(Encoding.ASCII.GetBytes(content), fileName).ShouldBe(expected);
    }

    private Task<ArchiveResult> Archive(SourceType sourceType) =>
        new ArchiveDocument(_fetcher, _store).RunAsync(new ArchiveRequest(ActionPage, sourceType, DryRun: false), CancellationToken.None);
}
