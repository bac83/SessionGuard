namespace Server.Ui.Models;

public sealed record ChildAgentSummary(
    IReadOnlyList<AgentStatus> Agents,
    string StatusLabel);

public static class ChildAgentSummaryBuilder
{
    public static ChildAgentSummary Build(DashboardSnapshot? snapshot, string childId)
    {
        var agents = snapshot?.Agents?
            .Where(agent => string.Equals(agent.ChildId, childId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(agent => agent.HostName, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];

        if (agents.Length == 0)
        {
            return new ChildAgentSummary(agents, "Awaiting agent");
        }

        if (agents.Any(agent => agent.IsSessionLocked))
        {
            return new ChildAgentSummary(agents, "Locked");
        }

        return new ChildAgentSummary(agents, agents.Any(agent => agent.IsOnline) ? "Online" : "Offline");
    }
}
