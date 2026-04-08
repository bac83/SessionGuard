using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Infrastructure.Persistence;
using Server.Infrastructure.Services;
using Shared.Contracts;

namespace Server.Api.Tests;

public sealed class RepositoryTests
{
    [Fact]
    public async Task SaveUsageReport_ComputesRemainingMinutesFromSQLiteState()
    {
        var sqlitePath = CreateSqlitePath();
        await using var dbContext = CreateDbContext(sqlitePath);
        var timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(new DateTimeOffset(2026, 4, 8, 8, 0, 0, TimeSpan.Zero));
        var repository = new SessionGuardRepository(dbContext, timeProvider);

        await repository.UpsertChildAsync(new UpsertChildRequest("child-01", "Sara", 90, true), CancellationToken.None);
        await repository.RegisterAgentAsync(new AgentRegistrationRequest("agent-01", "kid-laptop", "sara", "child-01", "1.0.0"), CancellationToken.None);

        var response = await repository.SaveUsageReportAsync(
            new UsageReportRequest("agent-01", "child-01", "sara", new DateOnly(2026, 4, 8), 35, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal("agent-01", response.AgentId);
        Assert.Equal(35, response.UsedMinutes);
        Assert.Equal(55, response.RemainingMinutes);
    }

    [Fact]
    public async Task SaveUsageReport_ReturnsZeroRemainingForDisabledChild()
    {
        var sqlitePath = CreateSqlitePath();
        await using var dbContext = CreateDbContext(sqlitePath);
        var timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(new DateTimeOffset(2026, 4, 8, 8, 0, 0, TimeSpan.Zero));
        var repository = new SessionGuardRepository(dbContext, timeProvider);

        await repository.UpsertChildAsync(new UpsertChildRequest("child-02", "Sara", 90, false), CancellationToken.None);
        await repository.RegisterAgentAsync(new AgentRegistrationRequest("agent-02", "kid-laptop", "sara", "child-02", "1.0.0"), CancellationToken.None);

        var response = await repository.SaveUsageReportAsync(
            new UsageReportRequest("agent-02", "child-02", "sara", new DateOnly(2026, 4, 8), 35, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(0, response.RemainingMinutes);
    }

    [Fact]
    public async Task UpsertChildAsync_ReturnsCurrentUsageAndRemainingMinutes()
    {
        var sqlitePath = CreateSqlitePath();
        await using var dbContext = CreateDbContext(sqlitePath);
        var timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(
            new DateTimeOffset(2026, 4, 8, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 9, 0, 0, TimeSpan.Zero));
        var repository = new SessionGuardRepository(dbContext, timeProvider);

        await repository.UpsertChildAsync(new UpsertChildRequest("child-03", "Mila", 120, true), CancellationToken.None);
        await repository.RegisterAgentAsync(new AgentRegistrationRequest("agent-03", "kid-laptop", "mila", "child-03", "1.0.0"), CancellationToken.None);
        await repository.SaveUsageReportAsync(
            new UsageReportRequest("agent-03", "child-03", "mila", new DateOnly(2026, 4, 8), 50, DateTimeOffset.UtcNow),
            CancellationToken.None);

        var updated = await repository.UpsertChildAsync(new UpsertChildRequest("child-03", "Mila", 100, true), CancellationToken.None);

        Assert.Equal(50, updated.CurrentUsageMinutes);
        Assert.Equal(50, updated.RemainingMinutes);
    }

    [Fact]
    public async Task GetPolicyAsync_UpdatesHeartbeatEvenWithoutPolicy()
    {
        var sqlitePath = CreateSqlitePath();
        await using var dbContext = CreateDbContext(sqlitePath);
        var timeProvider = Substitute.For<TimeProvider>();
        var before = new DateTimeOffset(2026, 4, 8, 8, 0, 0, TimeSpan.Zero);
        var after = new DateTimeOffset(2026, 4, 8, 8, 5, 0, TimeSpan.Zero);
        timeProvider.GetUtcNow().Returns(before, after);
        var repository = new SessionGuardRepository(dbContext, timeProvider);

        await repository.RegisterAgentAsync(new AgentRegistrationRequest("agent-04", "kid-laptop", "mila", null, "1.0.0"), CancellationToken.None);

        var response = await repository.GetPolicyAsync("agent-04", "missing-child", CancellationToken.None);
        var agent = await dbContext.Agents.SingleAsync(x => x.AgentId == "agent-04");

        Assert.Null(response.Policy);
        Assert.Equal(after, agent.LastSeenAtUtc);
    }

    private static string CreateSqlitePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "sessionguard-repository-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "sessionguard.db");
    }

    private static SessionGuardDbContext CreateDbContext(string sqlitePath)
    {
        var options = new DbContextOptionsBuilder<SessionGuardDbContext>()
            .UseSqlite($"Data Source={sqlitePath}")
            .Options;

        var dbContext = new SessionGuardDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }
}
