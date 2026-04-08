namespace Shared.Contracts;

public sealed record DashboardResponse(
    IReadOnlyList<ChildSummary> Children,
    IReadOnlyList<AgentStatusSummary> Agents);
