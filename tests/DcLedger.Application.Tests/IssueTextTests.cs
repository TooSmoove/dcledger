using Shouldly;
using Xunit;

namespace DcLedger.Application.Tests;

public sealed class IssueTextTests
{
    [Theory]
    [InlineData("@octocat", "@&#8203;octocat")]
    [InlineData("cc @dc-ledger/reviewers", "cc @&#8203;dc-ledger/reviewers")]
    [InlineData("a@b.org", "a@&#8203;b.org")]
    [InlineData("&#64;octocat", "&amp;\\#64;octocat")]
    [InlineData("@\u200Boctocat", "@&#8203;octocat")]
    public void Mentions_are_neutralized(string text, string expected)
    {
        IssueText.Inline(text).ShouldBe(expected);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "&lt;script&gt;alert\\(1\\)&lt;/script&gt;")]
    [InlineData("[click](https://evil.example)", "\\[click\\]\\(https\\://evil.example\\)")]
    [InlineData("a | b", "a \\| b")]
    [InlineData("line one\nline two\r\n# heading", "line one line two \\# heading")]
    [InlineData("**bold** _it_ `code` ~x~", "\\*\\*bold\\*\\* \\_it\\_ \\`code\\` \\~x\\~")]
    [InlineData("right-to-left \u202Eevil", "right-to-left evil")]
    [InlineData("   ", "—")]
    [InlineData(null, "—")]
    public void Inline_text_is_one_inert_line(string? text, string expected)
    {
        IssueText.Inline(text).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2026-439", "2026-439")]
    [InlineData(" 2026-439 ", "2026-439")]
    [InlineData("Z-26/01", "Z-2601")]
    [InlineData("@everyone", "everyone")]
    [InlineData("<b>17</b>", "b17b")]
    [InlineData("2026–439", "2026439")]
    [InlineData("---", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Case_numbers_keep_only_letters_digits_and_hyphens(string? text, string? expected)
    {
        IssueText.CaseNumber(text).ShouldBe(expected);
    }

    [Fact]
    public void Case_numbers_are_capped()
    {
        IssueText.CaseNumber(new string('9', 100))!.Length.ShouldBe(IssueText.MaxCaseNumberLength);
    }

    [Fact]
    public void Titles_break_mentions_and_drop_control_characters()
    {
        IssueText.Title(" Review: @octocat\n\tnow ").ShouldBe("Review: @\u200Boctocat now");
    }

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("say \"hi\" \\ bye", "\"say \\\"hi\\\" \\\\ bye\"")]
    [InlineData("two\nlines", "\"two lines\"")]
    public void Yaml_values_are_double_quoted_and_escaped(string text, string expected)
    {
        IssueText.YamlQuoted(text).ShouldBe(expected);
    }
}
