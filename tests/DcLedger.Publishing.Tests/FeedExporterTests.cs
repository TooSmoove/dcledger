using System.Globalization;
using System.Xml.Linq;
using DcLedger.Application;
using DcLedger.Domain;
using DcLedger.Publishing.Feed;
using DcLedger.Testing;
using Shouldly;
using Xunit;
using static VerifyXunit.Verifier;

namespace DcLedger.Publishing.Tests;

public sealed class FeedExporterTests
{
    private static readonly Uri Site = new("https://ncdcledger.org");
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    [Fact]
    public Task Feed_golden() => Verify(Feed(ExportSamples.Model()));

    [Fact]
    public void Feed_is_rss_2_with_the_required_channel_elements_and_a_self_link()
    {
        var rss = XDocument.Parse(Feed(ExportSamples.Model())).Root!;

        rss.Name.LocalName.ShouldBe("rss");
        rss.Attribute("version")!.Value.ShouldBe("2.0");
        var channel = rss.Element("channel")!;
        channel.Element("title")!.Value.ShouldBe("NC Data Center Ledger: Hearing Radar");
        channel.Element("link")!.Value.ShouldBe("https://ncdcledger.org/radar/");
        channel.Element("description")!.Value.ShouldNotBeNullOrWhiteSpace();
        var self = channel.Element(Atom + "link")!;
        (self.Attribute("href")!.Value, self.Attribute("rel")!.Value, self.Attribute("type")!.Value)
            .ShouldBe(("https://ncdcledger.org/feed.xml", "self", "application/rss+xml"));
    }

    [Fact]
    public void One_item_per_event_newest_first_with_rfc_822_dates()
    {
        var items = Items(ExportSamples.Model());

        items.Select(item => item.Element("link")!.Value).ShouldBe(
        [
            $"https://ncdcledger.org/items/{ExportSamples.November3.Id}/",
            $"https://ncdcledger.org/items/{ExportSamples.November5.Id}/",
            $"https://ncdcledger.org/items/{ExportSamples.October15.Id}/",
        ]);
        var dates = items.Select(item => DateTimeOffset.ParseExact(
            item.Element("pubDate")!.Value, "ddd, dd MMM yyyy HH:mm:ss '+0000'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)).ToList();
        dates[0].ShouldBe(ExportSamples.CityAgenda.RetrievedAtUtc, "the newest retrieval of the event's documents");
        dates.ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public void Guid_is_the_calendar_uid_and_sequence_so_a_published_change_is_a_new_item()
    {
        var model = ExportSamples.Model();
        var before = GuidOf(model, ExportSamples.November5.Id);
        var cancelled = model with
        {
            Events = [.. model.Events.Select(item => item.Event.Id == ExportSamples.November5.Id
                ? item with { Event = item.Event with { Status = EventStatus.Cancelled, Sequence = 1 } }
                : item)],
        };

        before.ShouldBe($"{ExportSamples.November5.Uid}/0");
        GuidOf(model, ExportSamples.November5.Id).ShouldBe(before, "unchanged events keep their GUID");
        GuidOf(cancelled, ExportSamples.November5.Id).ShouldBe($"{ExportSamples.November5.Uid}/1");
        Items(cancelled).Single(item => item.Element("guid")!.Value.EndsWith("/1", StringComparison.Ordinal)
                && item.Element("link")!.Value.Contains(ExportSamples.November5.Id, StringComparison.Ordinal))
            .Element("title")!.Value.ShouldBe("Cancelled: Data center moratorium decision (Fixture County)");
        Items(model).ShouldAllBe(item => item.Element("guid")!.Attribute("isPermaLink")!.Value == "false");
    }

    [Fact]
    public void Revised_meeting_is_marked_updated()
    {
        var model = ExportSamples.Model();
        var revised = model with
        {
            Events = [.. model.Events.Select(item => item.Event.Id == ExportSamples.November3.Id ? item with { Event = item.Event with { Sequence = 2 } } : item)],
        };

        Items(revised)[0].Element("title")!.Value.ShouldBe("Updated: Café district text amendment: data centers (Fixture City)");
    }

    [Fact]
    public void Description_carries_label_and_every_source_with_its_hash_as_escaped_html()
    {
        var october15 = Items(ExportSamples.Model())[2];
        var description = october15.Element("description")!.Value;

        october15.Element("title")!.Value.ShouldBe("Cancelled: Rezoning hearing, \"Project Example\"; phase 2 (Fixture County)");
        description.ShouldStartWith("<p>Fixture County, Board of Commissioners: Thursday, October 15, 2026, 5:30 PM Eastern.</p><p><strong>Cancelled.</strong></p>");
        description.ShouldContain("<p>Case: Z-26-01</p><p>Confirmed (official record)</p>");
        description.ShouldContain($"SHA-256 {ExportSamples.NewsStory.Id.Value})</li><li>");
        description.ShouldContain($"SHA-256 {ExportSamples.CountyAgenda1599.Id.Value})</li></ul>");
    }

    [Fact]
    public void Markup_in_ledger_text_is_escaped_for_html()
    {
        var model = ExportSamples.Model();
        var hostile = model with
        {
            Events = [.. model.Events.Select(item => item with { Event = item.Event with { Body = "<script>alert(1)</script> & co" } })],
        };

        var description = Items(hostile)[0].Element("description")!.Value;

        description.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt; &amp; co");
        description.ShouldNotContain("<script>");
    }

    [Fact]
    public void Empty_ledger_is_a_valid_feed_without_a_build_date()
    {
        var empty = ExportSamples.Model() with { Events = [] };

        var channel = XDocument.Parse(Feed(empty)).Root!.Element("channel")!;

        channel.Elements("item").ShouldBeEmpty();
        channel.Element("lastBuildDate").ShouldBeNull();
    }

    [Fact]
    public void Same_input_gives_the_same_bytes_utf8_lf_without_a_bom()
    {
        var first = FeedFile().Content.ToArray();
        var second = FeedFile().Content.ToArray();

        second.ShouldBe(first);
        first[0].ShouldBe((byte)'<');
        first.ShouldNotContain((byte)'\r');
        first[^1].ShouldBe((byte)'\n');
    }

    [Fact]
    public void Links_follow_the_configured_site()
    {
        var items = XDocument.Parse(TestFiles.Text(new FeedExporter(new Uri("https://preview.example.org")).Export(ExportSamples.Model()).ShouldHaveSingleItem())).Root!;

        items.Descendants("link").First().Value.ShouldBe("https://preview.example.org/radar/");
    }

    [Theory]
    [InlineData("ftp://ncdcledger.org")]
    [InlineData("/relative")]
    public void Site_url_must_be_absolute_http(string url)
    {
        Should.Throw<ArgumentException>(() => new FeedExporter(new Uri(url, UriKind.RelativeOrAbsolute)));
    }

    private static ExportFile FeedFile() => TestFiles.Single(new FeedExporter(Site).Export(ExportSamples.Model()), ExportPaths.Feed);

    private static string Feed(ExportModel model) => TestFiles.Text(TestFiles.Single(new FeedExporter(Site).Export(model), ExportPaths.Feed));

    private static List<XElement> Items(ExportModel model) => [.. XDocument.Parse(Feed(model)).Root!.Element("channel")!.Elements("item")];

    private static string GuidOf(ExportModel model, string eventId) =>
        Items(model).Single(item => item.Element("link")!.Value.Contains(eventId, StringComparison.Ordinal)).Element("guid")!.Value;
}
