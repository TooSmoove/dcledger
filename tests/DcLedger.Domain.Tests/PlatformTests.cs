using Shouldly;
using Xunit;

namespace DcLedger.Domain.Tests;

public sealed class PlatformTests
{
    [Theory]
    [InlineData(Platform.Legistar, "legistar")]
    [InlineData(Platform.Manual, "manual")]
    public void Tokens_are_fixed(Platform platform, string token)
    {
        // Event UIDs and storage keys are built from these; changing one breaks subscriptions and orphans state.
        platform.ToToken().ShouldBe(token);
    }

    [Fact]
    public void Every_platform_has_a_token()
    {
        foreach (var platform in Enum.GetValues<Platform>())
        {
            platform.ToToken().ShouldNotBeNullOrWhiteSpace();
        }
    }
}
