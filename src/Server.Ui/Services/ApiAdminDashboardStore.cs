using Server.Ui.Models;
using Shared.Contracts;

namespace Server.Ui.Services;

public sealed class ApiAdminDashboardStore(SessionGuardApiClient apiClient) : IAdminDashboardStore
{
    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var dashboard = await apiClient.GetDashboardAsync(cancellationToken);
        return new DashboardSnapshot(
            dashboard.Children.Select(MapChild).ToArray(),
            dashboard.Agents.Select(MapAgent).ToArray(),
            DateTimeOffset.UtcNow);
    }

    public async Task<ChildProfile> AddChildAsync(ChildProfileDraft draft, CancellationToken cancellationToken = default)
    {
        var child = await apiClient.SaveChildAsync(
            new UpsertChildRequest(draft.ChildId.Trim(), draft.DisplayName.Trim(), draft.DailyBudgetMinutes, draft.IsActive),
            cancellationToken);

        return MapChild(child);
    }

    public async Task<ChildProfile> UpdateChildBudgetAsync(string childId, int dailyBudgetMinutes, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var existing = snapshot.Children.FirstOrDefault(child => string.Equals(child.ChildId, childId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"No child profile exists for id '{childId}'.");

        var child = await apiClient.SaveChildAsync(
            new UpsertChildRequest(existing.ChildId, existing.DisplayName, dailyBudgetMinutes, existing.IsActive),
            cancellationToken);

        return MapChild(child);
    }

    public async Task<ChildProfile> SetChildActiveAsync(string childId, bool isActive, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var existing = snapshot.Children.FirstOrDefault(child => string.Equals(child.ChildId, childId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"No child profile exists for id '{childId}'.");

        var child = await apiClient.SaveChildAsync(
            new UpsertChildRequest(existing.ChildId, existing.DisplayName, existing.DailyBudgetMinutes, isActive),
            cancellationToken);

        return MapChild(child);
    }

    private static ChildProfile MapChild(ChildSummary child)
    {
        return new ChildProfile(
            child.ChildId,
            child.DisplayName,
            child.DailyLimitMinutes,
            child.IsEnabled,
            child.CurrentUsageMinutes,
            child.RemainingMinutes,
            child.UpdatedAtUtc);
    }

    private static AgentStatus MapAgent(AgentStatusSummary agent)
    {
        return new AgentStatus(
            agent.AgentId,
            agent.Hostname,
            agent.LocalUser ?? "unknown",
            agent.ChildId ?? "unassigned",
            agent.IsOnline,
            agent.IsSessionLocked,
            agent.UsedMinutesToday,
            agent.RemainingMinutes,
            agent.LastSeenAtUtc);
    }
}
