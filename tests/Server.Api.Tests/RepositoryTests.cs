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
