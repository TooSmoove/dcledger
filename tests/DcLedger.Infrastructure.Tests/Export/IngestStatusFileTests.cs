using System.Text;
using DcLedger.Application;
using DcLedger.Infrastructure.Export;
using Shouldly;
using Xunit;

namespace DcLedger.Infrastructure.Tests.Export;

public sealed class IngestStatusFileTests : IDisposable
{
    private const string Valid = """
        {
          "schema_version": 1,
          "generated_utc": "2026-11-05T14:47:00Z",
          "jurisdictions": [
            { "id": "guilford", "last_checked_utc": "2026-11-05T12:17:43Z" },
            { "id": "winston-salem", "last_checked_utc": null }
          ]
        }
        """;

    private readonly string _temp = Directory.CreateTempSubdirectory("dcl-status-").FullName;

    [Fact]
    public void A_valid_file_is_read()
    {
        var status = IngestStatusFile.Read(Write(Valid));

        status.GeneratedUtc.ShouldBe(new DateTimeOffset(2026, 11, 5, 14, 47, 0, TimeSpan.Zero));
        status.LastCheckedUtc["guilford"].ShouldBe(new DateTimeOffset(2026, 11, 5, 12, 17, 43, TimeSpan.Zero));
        status.LastCheckedUtc["winston-salem"].ShouldBeNull();
        status.LastCheckedUtc.Count.ShouldBe(2);
    }

    [Fact]
    public void Fractional_seconds_are_accepted()
    {
        var status = Parse(Valid.Replace("14:47:00Z", "14:47:00.25Z", StringComparison.Ordinal));

        status.GeneratedUtc.ShouldBe(new DateTimeOffset(2026, 11, 5, 14, 47, 0, 250, TimeSpan.Zero));
    }

    [Fact]
    public void An_empty_jurisdiction_list_is_valid()
    {
        Parse("""{ "schema_version": 1, "generated_utc": "2026-11-05T14:47:00Z", "jurisdictions": [] }""")
            .LastCheckedUtc.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{ "schema_version": 1, "generated_utc": "2026-11-05T14:47:00Z", "jurisdictions": [], "extra": 1 }""", "extra")]
    [InlineData("""{ "schema_version": 1, "generated_utc": "2026-11-05T14:47:00Z", "jurisdictions": [ { "id": "guilford", "last_checked_utc": null, "note": "x" } ] }""", "note")]
    [InlineData("""{ "schema_version": 1, "jurisdictions": [] }""", "generated_utc")]
    [InlineData("""{ "schema_version": 1, "generated_utc": "2026-11-05T14:47:00Z", "jurisdictions": [ { "id": "guilford" } ] }""", "last_checked_utc")]
    [InlineData("""{ "schema_version": 2, "generated_utc": "2026-11-05T14:47:00Z", "jurisdictions": [] }""", "schema_version")]
    [InlineData("""{ "schema_version": 1, "generated_utc": "2026-11-05T09:47:00-05:00", "jurisdictions": [] }""", "generated_utc")]
    [InlineData("""{ "schema_version": 1, "generated_utc": "2026-11-05 14:47:00", "jurisdictions": [] }""", "generated_utc")]
    [InlineData("""{ "schema_version": 1, "generated_utc": "2026-11-05T14:47:00Z", "jurisdictions": [ { "id": "Guilford", "last_checked_utc": null } ] }""", "Guilford")]
    [InlineData("""{ "schema_version": 1, "generated_utc": "2026-11-05T14:47:00Z", "jurisdictions": [ { "id": "a", "last_checked_utc": null }, { "id": "a", "last_checked_utc": null } ] }""", "more than once")]
    [InlineData("""{ "schema_version": 1, "generated_utc": "2026-11-05T14:47:00Z", "jurisdictions": null }""", "jurisdictions")]
    [InlineData("""[]""", "valid ingest status")]
    [InlineData("""not json""", "valid ingest status")]
    public void Malformed_files_are_rejected_with_a_reason(string json, string reason)
    {
        Should.Throw<IngestStatusException>(() => Parse(json)).Message.ShouldContain(reason);
    }

    [Fact]
    public void A_missing_file_is_reported()
    {
        var missing = Path.Combine(_temp, "nowhere.json");

        Should.Throw<IngestStatusException>(() => IngestStatusFile.Read(missing)).Message.ShouldContain("nowhere.json");
    }

    [Fact]
    public void An_oversized_file_is_refused()
    {
        var path = Path.Combine(_temp, "huge.json");
        File.WriteAllBytes(path, new byte[IngestStatusFile.MaxBytes + 1]);

        Should.Throw<IngestStatusException>(() => IngestStatusFile.Read(path)).Message.ShouldContain("larger than");
    }

    public void Dispose()
    {
        Directory.Delete(_temp, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static IngestStatus Parse(string json) => IngestStatusFile.Parse(Encoding.UTF8.GetBytes(json), "status.json");

    private string Write(string json)
    {
        var path = Path.Combine(_temp, "ingest-status.json");
        File.WriteAllText(path, json);
        return path;
    }
}
