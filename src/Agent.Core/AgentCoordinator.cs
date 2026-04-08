using Microsoft.Extensions.Logging;
using Shared.Contracts;

namespace Agent.Core;

public sealed class AgentCoordinator(
    AgentOptions options,
    IPolicyClient policyClient,
    IPolicyCache policyCache,
    IUserMappingProvider userMappingProvider,
    IUsageTracker usageTracker,
    ISessionController sessionController,
    IAgentStatusStore agentStatusStore,
    PolicyEvaluator policyEvaluator,
    TimeProvider timeProvider,
    ILogger<AgentCoordinator> logger)
{
    public async Task<IReadOnlyList<AgentStatusSnapshot>> RunOnceAsync(CancellationToken cancellationToken)
    {
        var snapshots = new List<AgentStatusSnapshot>();
        var mappings = await userMappingProvider.GetMappingsAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var usageDateUtc = DateOnly.FromDateTime(now.UtcDateTime);

        foreach (var mapping in mappings)
        {
            try
            {
                await policyClient.RegisterAsync(mapping, cancellationToken);
                var response = await TryFetchPolicyAsync(mapping, now, cancellationToken);
                var usedMinutes = await usageTracker.GetUsedMinutesAsync(mapping, usageDateUtc, cancellationToken);
                var evaluation = policyEvaluator.Evaluate(response.Policy, usedMinutes);

                if (evaluation.ShouldLock)
                {
                    await sessionController.LockAsync(mapping, cancellationToken);
                }

                var snapshot = new AgentStatusSnapshot(
                    options.AgentId,
                    mapping.LocalUser,
                    mapping.ChildId,
                    response.Policy is not null,
                    response.IsFromCache,
                    evaluation.ShouldLock,
                    evaluation.RemainingMinutes,
                    evaluation.UsedMinutes,
                    now,
                    response.Policy is null ? "No valid policy available." : null);

                snapshots.Add(snapshot);
                await agentStatusStore.SaveAsync(snapshot, cancellationToken);

                if (response.Policy is not null)
                {
                    await policyClient.ReportUsageAsync(
                        new UsageReportRequest(
                            options.AgentId,
                            mapping.ChildId,
                            mapping.LocalUser,
                            usageDateUtc,
                            evaluation.UsedMinutes,
                            now),
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Agent cycle failed for local user {LocalUser}", mapping.LocalUser);
                var failed = new AgentStatusSnapshot(
                    options.AgentId,
                    mapping.LocalUser,
                    mapping.ChildId,
                    false,
                    IsOfflineFailure(ex),
                    false,
                    null,
                    0,
                    now,
                    ex.Message);

                snapshots.Add(failed);
                await agentStatusStore.SaveAsync(failed, cancellationToken);
            }
        }

        return snapshots;
    }

    private static bool IsOfflineFailure(Exception exception) =>
        exception is HttpRequestException or TimeoutException or TaskCanceledException;

    private async Task<PolicyFetchResponse> TryFetchPolicyAsync(
        UserChildMapping mapping,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await policyClient.FetchPolicyAsync(mapping, cancellationToken);
            var authoritativeResponse = response with { IsFromCache = false };

            if (response.Policy is not null)
            {
                await policyCache.SaveAsync(
                    new CachedPolicyState(mapping.LocalUser, authoritativeResponse, now),
                    cancellationToken);
            }

            return authoritativeResponse;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falling back to cached policy for {LocalUser}", mapping.LocalUser);
        }

        var cached = await policyCache.LoadAsync(mapping.LocalUser, cancellationToken);
        if (cached?.Response is not null)
        {
            return cached.Response with { IsFromCache = true };
        }

        return new PolicyFetchResponse(options.AgentId, mapping.ChildId, null, now, IsFromCache: true);
    }
}
