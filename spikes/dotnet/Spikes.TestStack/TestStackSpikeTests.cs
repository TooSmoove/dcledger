using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;
using static VerifyXunit.Verifier;

namespace Spikes.TestStack;

/// <summary>Spike S10: xunit.v3 4.x + Verify.XunitV3 + Shouldly + WireMock.Net build and run together.</summary>
public class TestStackSpikeTests
{
    [Fact]
    public async Task Verify_snapshot_runs_under_xunit_v3()
    {
        await Verify(new { Uid = "spike-guilford-boc-20261105", StartsUtc = "2026-11-05T22:30:00Z" }).AutoVerify();
        Findings.Write("S10", "Verify.XunitV3 31.25.0 ran under xunit.v3 4.0.2");
    }

    [Fact]
    public async Task WireMock_serves_a_recorded_legistar_response()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/guilford/events").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""[{"EventId":1,"EventDate":"2026-11-05T00:00:00","EventTime":"5:30 PM"}]"""));

        using var http = new HttpClient();
        var body = await http.GetStringAsync($"{server.Url}/v1/guilford/events", TestContext.Current.CancellationToken);

        body.ShouldContain("5:30 PM");
        Findings.Write("S10", "WireMock.Net.Minimal 2.19.0 served a fixture");
    }
}
