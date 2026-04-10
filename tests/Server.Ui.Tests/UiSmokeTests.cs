using NSubstitute;
using Server.Ui.Models;
using Shared.Contracts;
using Server.Infrastructure.Services;
using Server.Ui.Services;

namespace Server.Ui.Tests;

public sealed class UiSmokeTests
{
    [Fact]
    public async Task GetSnapshotAsync_MapsDashboardPayload()
    {
        var repository = Substitute.For<ISessionGuardRepository>();
        repository.GetDashboardAsync(Arg.Any<CancellationToken>()).Returns(new DashboardResponse(
            [new ChildSummary("child-01", "Sara", 90, true, 15, 75, new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero))],
            [new AgentStatusSummary("agent-01", "1.2.3", "mond", "sara", "child-01", "policy-v1", 15, 75, false, true, new DateTimeOffset(2026, 4, 8, 12, 5, 0, TimeSpan.Zero), null)]));

        var store = new ApiAdminDashboardStore(repository);
        var dashboard = await store.GetSnapshotAsync();

        var child = Assert.Single(dashboard.Children);
        Assert.Equal("child-01", child.ChildId);
        Assert.Equal(90, child.DailyBudgetMinutes);
        var agent = Assert.Single(dashboard.Agents);
        Assert.Equal("1.2.3", agent.AgentVersion);
    }

    [Fact]
    public async Task AddChildAsync_TrimsDraftAndReturnsMappedChild()
    {
        var repository = Substitute.For<ISessionGuardRepository>();
        repository.UpsertChildAsync(Arg.Any<UpsertChildRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChildSummary("child-01", "Sara", 90, true, 0, 90, new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero)));

        var store = new ApiAdminDashboardStore(repository);
        var child = await store.AddChildAsync(new ChildProfileDraft
        {
            ChildId = " child-01 ",
            DisplayName = " Sara ",
            DailyBudgetMinutes = 90,
            IsActive = true
        });

        Assert.Equal("child-01", child.ChildId);
        await repository.Received(1).UpsertChildAsync(
            Arg.Is<UpsertChildRequest>(request =>
                request.ChildId == "child-01"
                && request.DisplayName == "Sara"
                && request.DailyLimitMinutes == 90
                && request.IsEnabled),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ChildAgentSummaryBuilder_ReturnsAssignedAgentsAndOnlineStatus()
    {
        var snapshot = new DashboardSnapshot(
            [
                new ChildProfile("child-01", "Sara", 90, true, 15, 75, DateTimeOffset.UtcNow),
                new ChildProfile("child-02", "Mila", 120, true, 0, 120, DateTimeOffset.UtcNow)
            ],
            [
                new AgentStatus("agent-01", "1.2.3", "mond", "sara", "child-01", true, false, 15, 75, DateTimeOffset.UtcNow),
                new AgentStatus("agent-02", "1.2.3", "tablet", "sara", "child-01", false, false, 15, 75, DateTimeOffset.UtcNow)
            ],
            DateTimeOffset.UtcNow);

        var summary = ChildAgentSummaryBuilder.Build(snapshot, "child-01");

        Assert.Equal(2, summary.Agents.Count);
        Assert.Equal("Online", summary.StatusLabel);
        Assert.Equal("mond", summary.Agents[0].HostName);
        Assert.Equal("tablet", summary.Agents[1].HostName);
    }

    [Fact]
    public void ChildAgentSummaryBuilder_ReturnsAwaitingAgentWhenNoAssignmentExists()
    {
        var snapshot = new DashboardSnapshot(
            [new ChildProfile("child-02", "Mila", 120, true, 0, 120, DateTimeOffset.UtcNow)],
            [],
            DateTimeOffset.UtcNow);

        var summary = ChildAgentSummaryBuilder.Build(snapshot, "child-02");

        Assert.Empty(summary.Agents);
        Assert.Equal("Awaiting agent", summary.StatusLabel);
    }

    [Fact]
    public void ChildAgentSummaryBuilder_ReturnsAwaitingAgentWhenSnapshotIsNull()
    {
        var summary = ChildAgentSummaryBuilder.Build(null, "child-02");

        Assert.Empty(summary.Agents);
        Assert.Equal("Awaiting agent", summary.StatusLabel);
    }
}
