using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure.Persistence;
using Shared.Contracts;

namespace Server.Infrastructure.Services;

public sealed class SessionGuardRepository(SessionGuardDbContext dbContext, TimeProvider timeProvider) : ISessionGuardRepository
{
    public async Task<IReadOnlyList<ChildSummary>> GetChildrenAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var children = await dbContext.Children
            .AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        var usageByChild = await GetLatestUsageByChildAsync(today, cancellationToken);

        return children
            .Select(child =>
            {
                var used = usageByChild.GetValueOrDefault(child.ChildId);
                var remaining = child.IsEnabled ? Math.Max(0, child.DailyLimitMinutes - used) : 0;
                return new ChildSummary(
                    child.ChildId,
                    child.DisplayName,
                    child.DailyLimitMinutes,
                    child.IsEnabled,
                    used,
                    remaining,
                    child.UpdatedAtUtc);
            })
            .ToArray();
    }

    public async Task<ChildSummary> UpsertChildAsync(UpsertChildRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ChildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);
        var childId = request.ChildId.Trim();

        var now = timeProvider.GetUtcNow();
        var entity = await dbContext.Children.SingleOrDefaultAsync(x => x.ChildId == childId, cancellationToken);
        if (entity is null)
        {
            entity = new ChildEntity { ChildId = childId };
            dbContext.Children.Add(entity);
        }

        entity.DisplayName = request.DisplayName.Trim();
        entity.DailyLimitMinutes = request.DailyLimitMinutes;
        entity.IsEnabled = request.IsEnabled;
        entity.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var usageByChild = await GetLatestUsageByChildAsync(today, cancellationToken);
        var used = usageByChild.GetValueOrDefault(entity.ChildId);
        var remaining = entity.IsEnabled ? Math.Max(0, entity.DailyLimitMinutes - used) : 0;
        return new ChildSummary(entity.ChildId, entity.DisplayName, entity.DailyLimitMinutes, entity.IsEnabled, used, remaining, entity.UpdatedAtUtc);
    }

    public async Task<DashboardResponse> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var children = await GetChildrenAsync(cancellationToken);
        var childLookup = children.ToDictionary(x => x.ChildId, StringComparer.OrdinalIgnoreCase);
        var usageByAgent = await GetLatestUsageByAgentAsync(today, cancellationToken);
        var agents = await dbContext.Agents
            .AsNoTracking()
            .OrderBy(x => x.AgentId)
            .ToListAsync(cancellationToken);

        var summaries = agents
            .Select(agent =>
            {
                var used = usageByAgent.GetValueOrDefault(agent.AgentId);
                var child = agent.ChildId is not null && childLookup.TryGetValue(agent.ChildId, out var resolvedChild)
                    ? resolvedChild
                    : null;
                int? remaining = child?.RemainingMinutes;
                var isOnline = agent.LastSeenAtUtc >= now.AddMinutes(-5);
                var isLocked = child is not null && remaining is 0 && (used > 0 || !child.IsEnabled);

                return new AgentStatusSummary(
                    agent.AgentId,
                    agent.Hostname,
                    agent.LocalUser,
                    agent.ChildId,
                    agent.LastPolicyVersion,
                    used,
                    remaining,
                    isLocked,
                    isOnline,
                    agent.LastSeenAtUtc,
                    agent.LastUsageReportAtUtc);
            })
            .ToArray();

        return new DashboardResponse(children, summaries);
    }

    public async Task<AgentRegistrationResponse> RegisterAgentAsync(AgentRegistrationRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AgentId);
        var now = timeProvider.GetUtcNow();
        var agentId = request.AgentId.Trim();
        var localUser = string.IsNullOrWhiteSpace(request.LocalUser) ? null : request.LocalUser.Trim();
        var childId = string.IsNullOrWhiteSpace(request.ChildId) ? null : request.ChildId.Trim();
        var entity = await dbContext.Agents.SingleOrDefaultAsync(x => x.AgentId == agentId, cancellationToken);
        if (entity is null)
        {
            entity = new AgentEntity { AgentId = agentId };
            dbContext.Agents.Add(entity);
        }

        entity.Hostname = request.Hostname.Trim();
        entity.LocalUser = localUser;
        entity.ChildId = childId;
        entity.AgentVersion = request.AgentVersion;
        entity.LastSeenAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return new AgentRegistrationResponse(entity.AgentId, now);
    }

    public async Task<PolicyFetchResponse> GetPolicyAsync(string agentId, string? childId, CancellationToken cancellationToken)
    {
        var normalizedAgentId = agentId.Trim();
        var normalizedChildId = childId?.Trim();
        var now = timeProvider.GetUtcNow();
        var agent = await dbContext.Agents.SingleOrDefaultAsync(x => x.AgentId == normalizedAgentId, cancellationToken);
        var resolvedChildId = string.IsNullOrWhiteSpace(normalizedChildId) ? agent?.ChildId : normalizedChildId;
        ChildPolicy? policy = null;

        if (agent is not null)
        {
            agent.LastSeenAtUtc = now;
        }

        if (!string.IsNullOrWhiteSpace(resolvedChildId))
        {
            var child = await dbContext.Children.SingleOrDefaultAsync(x => x.ChildId == resolvedChildId, cancellationToken);
            if (child is not null)
            {
                var version = child.UpdatedAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                policy = new ChildPolicy(
                    child.ChildId,
                    child.DailyLimitMinutes,
                    child.IsEnabled,
                    version,
                    DateOnly.FromDateTime(now.UtcDateTime),
                    child.UpdatedAtUtc);

                if (agent is not null)
                {
                    agent.LastPolicyVersion = version;
                }
            }
        }

        if (agent is not null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new PolicyFetchResponse(normalizedAgentId, resolvedChildId, policy, now);
    }

    public async Task<UsageReportResponse> SaveUsageReportAsync(UsageReportRequest request, CancellationToken cancellationToken)
    {
        var normalizedAgentId = request.AgentId.Trim();
        var normalizedChildId = request.ChildId.Trim();
        var now = timeProvider.GetUtcNow();
        var agent = await dbContext.Agents.SingleAsync(x => x.AgentId == normalizedAgentId, cancellationToken);

        var existing = await dbContext.UsageReports.SingleOrDefaultAsync(
            x => x.AgentId == normalizedAgentId && x.UsageDateUtc == request.UsageDateUtc,
            cancellationToken);

        if (existing is null)
        {
            existing = new UsageReportEntity
            {
                AgentId = normalizedAgentId,
                UsageDateUtc = request.UsageDateUtc
            };
            dbContext.UsageReports.Add(existing);
        }

        existing.ChildId = normalizedChildId;
        existing.LocalUser = request.LocalUser;
        existing.UsedMinutes = request.UsedMinutes;
        existing.ReportedAtUtc = now;

        agent.LastSeenAtUtc = now;
        agent.LastUsageReportAtUtc = now;

        var child = await dbContext.Children.SingleOrDefaultAsync(x => x.ChildId == normalizedChildId, cancellationToken);
        int? remainingMinutes = child is null
            ? null
            : child.IsEnabled
                ? Math.Max(0, child.DailyLimitMinutes - request.UsedMinutes)
                : 0;

        await dbContext.SaveChangesAsync(cancellationToken);
        return new UsageReportResponse(normalizedAgentId, normalizedChildId, request.UsedMinutes, remainingMinutes, now);
    }

    private async Task<Dictionary<string, int>> GetLatestUsageByChildAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var reports = await dbContext.UsageReports
            .AsNoTracking()
            .Where(x => x.UsageDateUtc == today)
            .ToListAsync(cancellationToken);

        return reports
            .GroupBy(x => x.ChildId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.ReportedAtUtc).First().UsedMinutes,
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, int>> GetLatestUsageByAgentAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var reports = await dbContext.UsageReports
            .AsNoTracking()
            .Where(x => x.UsageDateUtc == today)
            .ToListAsync(cancellationToken);

        return reports
            .GroupBy(x => x.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.ReportedAtUtc).First().UsedMinutes,
                StringComparer.OrdinalIgnoreCase);
    }
}
