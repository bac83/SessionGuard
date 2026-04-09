using Agent.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Contracts;

namespace Agent.Core.Tests;

public sealed class AgentCoordinatorTests
{
    [Fact]
    public async Task RunOnceAsync_UsesServerPolicyAndReportsUsage()
    {
        var now = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var options = new AgentOptions { AgentId = "agent-01" };
        var mapping = new UserChildMapping("alice", "child-01");
        var policyClient = Substitute.For<IPolicyClient>();
        var policyCache = Substitute.For<IPolicyCache>();
        var mappingProvider = Substitute.For<IUserMappingProvider>();
        var usageTracker = Substitute.For<IUsageTracker>();
        var sessionController = Substitute.For<ISessionController>();
        var statusStore = Substitute.For<IAgentStatusStore>();

        mappingProvider.GetMappingsAsync(Arg.Any<CancellationToken>()).Returns([mapping]);
        policyClient.FetchPolicyAsync(mapping, Arg.Any<CancellationToken>())
            .Returns(new PolicyFetchResponse(
                "agent-01",
                "child-01",
                new ChildPolicy("child-01", 60, true, "v1", new DateOnly(2026, 4, 8), now),
                now));
        usageTracker.GetUsedMinutesAsync(mapping, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(15);
        policyClient.ReportUsageAsync(Arg.Any<UsageReportRequest>(), Arg.Any<CancellationToken>())
            .Returns(new UsageReportResponse("agent-01", "child-01", 15, 45, now));

        var coordinator = new AgentCoordinator(
            options,
            policyClient,
            policyCache,
            mappingProvider,
            usageTracker,
            sessionController,
            statusStore,
            new PolicyEvaluator(),
            new FixedTimeProvider(now),
            NullLogger<AgentCoordinator>.Instance);

        var result = await coordinator.RunOnceAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.False(result[0].IsOfflineMode);
        Assert.Equal(45, result[0].RemainingMinutes);
        await policyCache.Received(1).SaveAsync(Arg.Any<CachedPolicyState>(), Arg.Any<CancellationToken>());
        await policyClient.Received(1).ReportUsageAsync(Arg.Is<UsageReportRequest>(x => x.UsedMinutes == 15), Arg.Any<CancellationToken>());
        await sessionController.DidNotReceive().LockAsync(Arg.Any<UserChildMapping>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_FallsBackToCachedPolicyAndLocksWhenBudgetIsExhausted()
    {
        var now = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var options = new AgentOptions { AgentId = "agent-01" };
        var mapping = new UserChildMapping("alice", "child-01");
        var policyClient = Substitute.For<IPolicyClient>();
        var policyCache = Substitute.For<IPolicyCache>();
        var mappingProvider = Substitute.For<IUserMappingProvider>();
        var usageTracker = Substitute.For<IUsageTracker>();
        var sessionController = Substitute.For<ISessionController>();
        var statusStore = Substitute.For<IAgentStatusStore>();

        mappingProvider.GetMappingsAsync(Arg.Any<CancellationToken>()).Returns([mapping]);
        policyClient.FetchPolicyAsync(mapping, Arg.Any<CancellationToken>()).Returns<Task<PolicyFetchResponse>>(_ => throw new HttpRequestException("offline"));
        policyCache.LoadAsync(mapping.LocalUser, Arg.Any<CancellationToken>())
            .Returns(new CachedPolicyState(
                mapping.LocalUser,
                new PolicyFetchResponse(
                    "agent-01",
                    "child-01",
                    new ChildPolicy("child-01", 30, true, "v2", new DateOnly(2026, 4, 8), now.AddMinutes(-5)),
                    now.AddMinutes(-5),
                    true),
                now.AddMinutes(-5)));
        usageTracker.GetUsedMinutesAsync(mapping, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(30);

        var coordinator = new AgentCoordinator(
            options,
            policyClient,
            policyCache,
            mappingProvider,
            usageTracker,
            sessionController,
            statusStore,
            new PolicyEvaluator(),
            new FixedTimeProvider(now),
            NullLogger<AgentCoordinator>.Instance);

        var result = await coordinator.RunOnceAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.True(result[0].IsOfflineMode);
        Assert.True(result[0].IsLocked);
        await sessionController.Received(1).LockAsync(mapping, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_UsesAuthoritativeEmptyPolicyWithoutFallingBackToCache()
    {
        var now = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var options = new AgentOptions { AgentId = "agent-01" };
        var mapping = new UserChildMapping("alice", "child-01");
        var policyClient = Substitute.For<IPolicyClient>();
        var policyCache = Substitute.For<IPolicyCache>();
        var mappingProvider = Substitute.For<IUserMappingProvider>();
        var usageTracker = Substitute.For<IUsageTracker>();
        var sessionController = Substitute.For<ISessionController>();
        var statusStore = Substitute.For<IAgentStatusStore>();

        mappingProvider.GetMappingsAsync(Arg.Any<CancellationToken>()).Returns([mapping]);
        policyClient.FetchPolicyAsync(mapping, Arg.Any<CancellationToken>())
            .Returns(new PolicyFetchResponse("agent-01", "child-01", null, now));
        policyCache.LoadAsync(mapping.LocalUser, Arg.Any<CancellationToken>())
            .Returns(new CachedPolicyState(
                mapping.LocalUser,
                new PolicyFetchResponse(
                    "agent-01",
                    "child-01",
                    new ChildPolicy("child-01", 30, true, "v-cache", new DateOnly(2026, 4, 8), now.AddMinutes(-5)),
                    now.AddMinutes(-5),
                    true),
                now.AddMinutes(-5)));
        usageTracker.GetUsedMinutesAsync(mapping, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(5);

        var coordinator = new AgentCoordinator(
            options,
            policyClient,
            policyCache,
            mappingProvider,
            usageTracker,
            sessionController,
            statusStore,
            new PolicyEvaluator(),
            new FixedTimeProvider(now),
            NullLogger<AgentCoordinator>.Instance);

        var result = await coordinator.RunOnceAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.False(result[0].IsOfflineMode);
        Assert.False(result[0].PolicyAvailable);
        await policyCache.DidNotReceive().LoadAsync(mapping.LocalUser, Arg.Any<CancellationToken>());
        await policyCache.DidNotReceive().SaveAsync(Arg.Any<CachedPolicyState>(), Arg.Any<CancellationToken>());
        await policyClient.DidNotReceive().ReportUsageAsync(Arg.Any<UsageReportRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_DoesNotMarkLockFailuresAsOfflineMode()
    {
        var now = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var options = new AgentOptions { AgentId = "agent-01" };
        var mapping = new UserChildMapping("alice", "child-01");
        var policyClient = Substitute.For<IPolicyClient>();
        var policyCache = Substitute.For<IPolicyCache>();
        var mappingProvider = Substitute.For<IUserMappingProvider>();
        var usageTracker = Substitute.For<IUsageTracker>();
        var sessionController = Substitute.For<ISessionController>();
        var statusStore = Substitute.For<IAgentStatusStore>();

        mappingProvider.GetMappingsAsync(Arg.Any<CancellationToken>()).Returns([mapping]);
        policyClient.FetchPolicyAsync(mapping, Arg.Any<CancellationToken>())
            .Returns(new PolicyFetchResponse(
                "agent-01",
                "child-01",
                new ChildPolicy("child-01", 30, true, "v1", new DateOnly(2026, 4, 8), now),
                now));
        usageTracker.GetUsedMinutesAsync(mapping, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(30);
        policyClient.ReportUsageAsync(Arg.Any<UsageReportRequest>(), Arg.Any<CancellationToken>())
            .Returns(new UsageReportResponse("agent-01", "child-01", 30, 0, now));
        sessionController.LockAsync(mapping, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("loginctl failed"));

        var coordinator = new AgentCoordinator(
            options,
            policyClient,
            policyCache,
            mappingProvider,
            usageTracker,
            sessionController,
            statusStore,
            new PolicyEvaluator(),
            new FixedTimeProvider(now),
            NullLogger<AgentCoordinator>.Instance);

        var result = await coordinator.RunOnceAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.False(result[0].IsOfflineMode);
        Assert.Equal("loginctl failed", result[0].Message);
    }

    [Fact]
    public async Task RunOnceAsync_LocksImmediatelyWhenPolicyIsPaused()
    {
        var now = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var options = new AgentOptions { AgentId = "agent-01" };
        var mapping = new UserChildMapping("alice", "child-01");
        var policyClient = Substitute.For<IPolicyClient>();
        var policyCache = Substitute.For<IPolicyCache>();
        var mappingProvider = Substitute.For<IUserMappingProvider>();
        var usageTracker = Substitute.For<IUsageTracker>();
        var sessionController = Substitute.For<ISessionController>();
        var statusStore = Substitute.For<IAgentStatusStore>();

        mappingProvider.GetMappingsAsync(Arg.Any<CancellationToken>()).Returns([mapping]);
        policyClient.FetchPolicyAsync(mapping, Arg.Any<CancellationToken>())
            .Returns(new PolicyFetchResponse(
                "agent-01",
                "child-01",
                new ChildPolicy("child-01", 60, false, "v2", new DateOnly(2026, 4, 8), now),
                now));
        usageTracker.GetUsedMinutesAsync(mapping, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(12);
        policyClient.ReportUsageAsync(Arg.Any<UsageReportRequest>(), Arg.Any<CancellationToken>())
            .Returns(new UsageReportResponse("agent-01", "child-01", 12, 0, now));

        var coordinator = new AgentCoordinator(
            options,
            policyClient,
            policyCache,
            mappingProvider,
            usageTracker,
            sessionController,
            statusStore,
            new PolicyEvaluator(),
            new FixedTimeProvider(now),
            NullLogger<AgentCoordinator>.Instance);

        var result = await coordinator.RunOnceAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.True(result[0].IsLocked);
        Assert.Equal(0, result[0].RemainingMinutes);
        await sessionController.Received(1).LockAsync(mapping, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_LocksWhenServerUsageResponseReportsZeroRemaining()
    {
        var now = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var options = new AgentOptions { AgentId = "agent-01" };
        var mapping = new UserChildMapping("alice", "child-01");
        var policyClient = Substitute.For<IPolicyClient>();
        var policyCache = Substitute.For<IPolicyCache>();
        var mappingProvider = Substitute.For<IUserMappingProvider>();
        var usageTracker = Substitute.For<IUsageTracker>();
        var sessionController = Substitute.For<ISessionController>();
        var statusStore = Substitute.For<IAgentStatusStore>();

        mappingProvider.GetMappingsAsync(Arg.Any<CancellationToken>()).Returns([mapping]);
        policyClient.FetchPolicyAsync(mapping, Arg.Any<CancellationToken>())
            .Returns(new PolicyFetchResponse(
                "agent-01",
                "child-01",
                new ChildPolicy("child-01", 60, true, "v1", new DateOnly(2026, 4, 8), now),
                now));
        usageTracker.GetUsedMinutesAsync(mapping, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(15);
        policyClient.ReportUsageAsync(Arg.Any<UsageReportRequest>(), Arg.Any<CancellationToken>())
            .Returns(new UsageReportResponse("agent-01", "child-01", 15, 0, now));

        var coordinator = new AgentCoordinator(
            options,
            policyClient,
            policyCache,
            mappingProvider,
            usageTracker,
            sessionController,
            statusStore,
            new PolicyEvaluator(),
            new FixedTimeProvider(now),
            NullLogger<AgentCoordinator>.Instance);

        var result = await coordinator.RunOnceAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.True(result[0].IsLocked);
        Assert.Equal(0, result[0].RemainingMinutes);
        await sessionController.Received(1).LockAsync(mapping, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_DoesNotReportUsageWhenUsingCachedPolicy()
    {
        var now = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var options = new AgentOptions { AgentId = "agent-01" };
        var mapping = new UserChildMapping("alice", "child-01");
        var policyClient = Substitute.For<IPolicyClient>();
        var policyCache = Substitute.For<IPolicyCache>();
        var mappingProvider = Substitute.For<IUserMappingProvider>();
        var usageTracker = Substitute.For<IUsageTracker>();
        var sessionController = Substitute.For<ISessionController>();
        var statusStore = Substitute.For<IAgentStatusStore>();

        mappingProvider.GetMappingsAsync(Arg.Any<CancellationToken>()).Returns([mapping]);
        policyClient.FetchPolicyAsync(mapping, Arg.Any<CancellationToken>()).Returns<Task<PolicyFetchResponse>>(_ => throw new HttpRequestException("offline"));
        policyCache.LoadAsync(mapping.LocalUser, Arg.Any<CancellationToken>())
            .Returns(new CachedPolicyState(
                mapping.LocalUser,
                new PolicyFetchResponse(
                    "agent-01",
                    "child-01",
                    new ChildPolicy("child-01", 30, true, "v2", new DateOnly(2026, 4, 8), now.AddMinutes(-5)),
                    now.AddMinutes(-5),
                    true),
                now.AddMinutes(-5)));
        usageTracker.GetUsedMinutesAsync(mapping, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(30);

        var coordinator = new AgentCoordinator(
            options,
            policyClient,
            policyCache,
            mappingProvider,
            usageTracker,
            sessionController,
            statusStore,
            new PolicyEvaluator(),
            new FixedTimeProvider(now),
            NullLogger<AgentCoordinator>.Instance);

        var result = await coordinator.RunOnceAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.True(result[0].IsLocked);
        await sessionController.Received(1).LockAsync(mapping, Arg.Any<CancellationToken>());
        await policyClient.DidNotReceive().ReportUsageAsync(Arg.Any<UsageReportRequest>(), Arg.Any<CancellationToken>());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
