using Shared.Contracts;

namespace Server.Infrastructure.Services;

public interface ISessionGuardRepository
{
    Task<IReadOnlyList<ChildSummary>> GetChildrenAsync(CancellationToken cancellationToken = default);
    Task<ChildSummary> UpsertChildAsync(UpsertChildRequest request, CancellationToken cancellationToken = default);
    Task<DashboardResponse> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<AgentRegistrationResponse> RegisterAgentAsync(AgentRegistrationRequest request, CancellationToken cancellationToken = default);
    Task<PolicyFetchResponse> GetPolicyAsync(string agentId, string? childId, CancellationToken cancellationToken = default);
    Task<UsageReportResponse> SaveUsageReportAsync(UsageReportRequest request, CancellationToken cancellationToken = default);
}
