using System.Text;
using Shouldly;
using Xunit;

namespace DcLedger.Domain.Tests;

public sealed class Sha256HashTests
{
    private const string EmptyInputHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Fact]
    public void Hash_of_empty_input_matches_the_known_digest()
    {
        Sha256Hash.Compute([]).Value.ShouldBe(EmptyInputHash);
    }

    [Fact]
    public void Lowercase_hex_digest_parses_and_round_trips()
    {
        Sha256Hash.TryParse(EmptyInputHash, out var hash).ShouldBeTrue();

        hash.ShouldBe(Sha256Hash.Compute(Encoding.UTF8.GetBytes(string.Empty)));
        hash.ToString().ShouldBe(EmptyInputHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855a")]
    [InlineData("g3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void Anything_but_64_lowercase_hex_characters_is_rejected(string? text)
    {
        Sha256Hash.TryParse(text, out _).ShouldBeFalse();
    }
}
