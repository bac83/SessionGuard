using Shared.Contracts;

namespace Agent.Core;

public interface IPolicyClient
{
    Task RegisterAsync(UserChildMapping mapping, CancellationToken cancellationToken);
    Task<PolicyFetchResponse> FetchPolicyAsync(UserChildMapping mapping, CancellationToken cancellationToken);
    Task<UsageReportResponse> ReportUsageAsync(UsageReportRequest request, CancellationToken cancellationToken);
}

public interface IPolicyCache
{
    Task<CachedPolicyState?> LoadAsync(string localUser, CancellationToken cancellationToken);
    Task SaveAsync(CachedPolicyState cachedPolicyState, CancellationToken cancellationToken);
}

public interface IUserMappingProvider
{
    Task<IReadOnlyList<UserChildMapping>> GetMappingsAsync(CancellationToken cancellationToken);
}

public interface IUsageTracker
{
    Task<int> GetUsedMinutesAsync(UserChildMapping mapping, DateOnly usageDateUtc, CancellationToken cancellationToken);
}

public interface ISessionController
{
    Task LockAsync(UserChildMapping mapping, CancellationToken cancellationToken);
}

public interface IAgentStatusStore
{
    Task SaveAsync(AgentStatusSnapshot snapshot, CancellationToken cancellationToken);
}
