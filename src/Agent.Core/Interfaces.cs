using Shared.Contracts;

namespace Agent.Core;

public interface IPolicyClient
{
    Task RegisterAsync(UserChildMapping mapping, CancellationToken cancellationToken);
    Task<PolicyFetchResponse> FetchPolicyAsync(UserChildMapping mapping, CancellationToken cancellationToken);
    Task ReportUsageAsync(UsageReportRequest request, CancellationToken cancellationToken);
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

public sealed record SessionState(bool IsActive, bool IsLocked, string? UserName)
{
    public static SessionState Inactive(string? userName = null) => new(false, true, userName);
}

public interface ISessionStateProvider
{
    Task<SessionState> GetStateAsync(CancellationToken cancellationToken = default);
}

public interface ISessionController
{
    Task LockAsync(UserChildMapping mapping, CancellationToken cancellationToken);
}

public interface IAgentStatusStore
{
    Task SaveAsync(AgentStatusSnapshot snapshot, CancellationToken cancellationToken);
}
