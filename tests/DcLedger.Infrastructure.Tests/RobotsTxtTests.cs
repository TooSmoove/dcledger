using DcLedger.Infrastructure.Fetching;
using Shouldly;
using Xunit;

namespace DcLedger.Infrastructure.Tests;

public sealed class RobotsTxtTests
{
    private const string Token = "dc-ledger";

    [Fact]
    public void Group_for_our_token_wins_over_the_wildcard_group()
    {
        var robots = RobotsTxt.Parse("""
            User-agent: *
            Disallow: /

            User-agent: DC-Ledger
            Disallow: /private
            """, Token);

        robots.IsAllowed(Url("/agenda")).ShouldBeTrue();
        robots.IsAllowed(Url("/private/x")).ShouldBeFalse();
    }

    [Fact]
    public void Wildcard_group_applies_when_nothing_names_us()
    {
        var robots = RobotsTxt.Parse("User-agent: PetalBot\nDisallow: /\n\nUser-agent: *\nDisallow: /search", Token);

        robots.IsAllowed(Url("/agenda")).ShouldBeTrue();
        robots.IsAllowed(Url("/search?q=x")).ShouldBeFalse();
    }

    [Fact]
    public void Group_naming_only_another_crawler_does_not_restrict_us()
    {
        // pub-greensboro-nc.escribemeetings.com, spike S9.
        RobotsTxt.Parse("User-agent: PetalBot\nDisallow: /", Token).IsAllowed(Url("/anything")).ShouldBeTrue();
    }

    [Fact]
    public void Longest_matching_rule_wins()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /docs\nAllow: /docs/public", Token);

        robots.IsAllowed(Url("/docs/public/agenda.pdf")).ShouldBeTrue();
        robots.IsAllowed(Url("/docs/private.pdf")).ShouldBeFalse();
    }

    [Fact]
    public void Allow_wins_a_tie()
    {
        RobotsTxt.Parse("User-agent: *\nDisallow: /page\nAllow: /page", Token).IsAllowed(Url("/page")).ShouldBeTrue();
    }

    [Fact]
    public void Wildcards_and_end_anchor_are_supported()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /*.pdf$", Token);

        robots.IsAllowed(Url("/files/agenda.pdf")).ShouldBeFalse();
        robots.IsAllowed(Url("/files/agenda.pdf?download=1")).ShouldBeTrue();
        robots.IsAllowed(Url("/files/agenda.html")).ShouldBeTrue();
    }

    [Fact]
    public void Empty_disallow_comments_and_unknown_keys_change_nothing()
    {
        var robots = RobotsTxt.Parse("# hello\nUser-agent: * # everyone\nCrawl-delay: 10\nSitemap: https://example.org/s.xml\nDisallow:", Token);

        robots.IsAllowed(Url("/anything")).ShouldBeTrue();
    }

    [Fact]
    public void Robots_txt_itself_is_always_allowed()
    {
        RobotsTxt.DisallowAll.IsAllowed(Url("/robots.txt")).ShouldBeTrue();
        RobotsTxt.DisallowAll.IsAllowed(Url("/agenda")).ShouldBeFalse();
    }

    [Fact]
    public void Regex_metacharacters_in_patterns_are_literal()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /View.ashx?M=A", Token);

        robots.IsAllowed(Url("/View.ashx?M=A&ID=1")).ShouldBeFalse();
        robots.IsAllowed(Url("/ViewXashx?M=A")).ShouldBeTrue();
    }

    private static Uri Url(string pathAndQuery) => new("https://example.org" + pathAndQuery);
}
