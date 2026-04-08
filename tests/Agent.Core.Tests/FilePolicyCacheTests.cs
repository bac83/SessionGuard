using Agent.Core;
using Shared.Contracts;

namespace Agent.Core.Tests;

public sealed class FilePolicyCacheTests
{
    [Fact]
    public async Task SaveAsync_OverwritesExistingCachedPolicy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sessionguard-policy-cache-tests", Guid.NewGuid().ToString("N"));
        var cache = new FilePolicyCache(directory);
        var localUser = "alice";
        var firstTime = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var secondTime = firstTime.AddMinutes(5);

        var first = new CachedPolicyState(
            localUser,
            new PolicyFetchResponse(
                "agent-01",
                "child-01",
                new ChildPolicy("child-01", 60, true, "v1", new DateOnly(2026, 4, 8), firstTime),
                firstTime),
            firstTime);

        var second = new CachedPolicyState(
            localUser,
            new PolicyFetchResponse(
                "agent-01",
                "child-01",
                new ChildPolicy("child-01", 30, true, "v2", new DateOnly(2026, 4, 8), secondTime),
                secondTime),
            secondTime);

        await cache.SaveAsync(first, CancellationToken.None);
        await cache.SaveAsync(second, CancellationToken.None);

        var saved = await cache.LoadAsync(localUser, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal(30, saved!.Response.Policy!.DailyLimitMinutes);
        Assert.Equal("v2", saved.Response.Policy.Version);
    }

    [Fact]
    public async Task SaveAsync_SanitizesLocalUserIntoSafeCacheFileName()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sessionguard-policy-cache-tests", Guid.NewGuid().ToString("N"));
        var cache = new FilePolicyCache(directory);
        var localUser = "../alice/../../danger";
        var timestamp = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var cached = new CachedPolicyState(
            localUser,
            new PolicyFetchResponse(
                "agent-01",
                "child-01",
                new ChildPolicy("child-01", 60, true, "v1", new DateOnly(2026, 4, 8), timestamp),
                timestamp),
            timestamp);

        await cache.SaveAsync(cached, CancellationToken.None);

        var files = Directory.GetFiles(directory, "*.policy.json", SearchOption.TopDirectoryOnly);
        var saved = await cache.LoadAsync(localUser, CancellationToken.None);

        Assert.Single(files);
        Assert.DoesNotContain("alice", Path.GetFileName(files[0]), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(saved);
        Assert.Equal("v1", saved!.Response.Policy!.Version);
    }
}
