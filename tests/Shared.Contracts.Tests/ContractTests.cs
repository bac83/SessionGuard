using System.Text.Json;
using Shared.Contracts;

namespace Shared.Contracts.Tests;

public sealed class ContractTests
{
    [Fact]
    public void PolicyFetchResponse_RoundTripsThroughSourceGeneratedJsonContext()
    {
        var response = new PolicyFetchResponse(
            "agent-01",
            "child-01",
            new ChildPolicy(
                "child-01",
                120,
                true,
                "child-01:1712577600",
                new DateOnly(2026, 4, 8),
                new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero)),
            new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(response, SessionGuardJsonContext.Default.PolicyFetchResponse);
        var roundTripped = JsonSerializer.Deserialize(json, SessionGuardJsonContext.Default.PolicyFetchResponse);

        Assert.Equal(response, roundTripped);
    }

    [Fact]
    public void CachedPolicyState_PreservesOfflineFallbackPayload()
    {
        var cached = new CachedPolicyState(
            "alice",
            new PolicyFetchResponse(
                "agent-01",
                "child-01",
                new ChildPolicy(
                    "child-01",
                    90,
                    true,
                    "child-01:1712577600",
                    new DateOnly(2026, 4, 8),
                    new DateTimeOffset(2026, 4, 8, 9, 0, 0, TimeSpan.Zero)),
                new DateTimeOffset(2026, 4, 8, 9, 0, 0, TimeSpan.Zero)),
            new DateTimeOffset(2026, 4, 8, 9, 1, 0, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(cached, SessionGuardJsonContext.Default.CachedPolicyState);
        var roundTripped = JsonSerializer.Deserialize(json, SessionGuardJsonContext.Default.CachedPolicyState);

        Assert.Equal(cached, roundTripped);
    }
}
