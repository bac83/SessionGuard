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
                UsageReportResponse? usageResponse = null;
                string? message = null;

                if (response.Policy is not null && !response.IsFromCache)
                {
                    try
                    {
                        usageResponse = await policyClient.ReportUsageAsync(
                            new UsageReportRequest(
                                options.AgentId,
                                mapping.ChildId,
                                mapping.LocalUser,
                                usageDateUtc,
                                evaluation.UsedMinutes,
                                now),
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Usage reporting failed for local user {LocalUser}", mapping.LocalUser);
                        message = $"Usage reporting failed: {ex.Message}";
                    }
                }

                var shouldLock = evaluation.ShouldLock || usageResponse?.RemainingMinutes is 0;
                var remainingMinutes = usageResponse?.RemainingMinutes ?? evaluation.RemainingMinutes;
                var policy = response.Policy;
                var isLocked = false;

                logger.LogInformation(
                    "Policy decision for {LocalUser}/{ChildId}: policyAvailable={PolicyAvailable}, fromCache={FromCache}, policyVersion={PolicyVersion}, enabled={PolicyEnabled}, dailyLimit={DailyLimitMinutes}, used={UsedMinutes}, remaining={RemainingMinutes}, usageReported={UsageReported}, shouldLock={ShouldLock}",
                    mapping.LocalUser,
                    mapping.ChildId,
                    policy is not null,
                    response.IsFromCache,
                    policy?.Version ?? "none",
                    policy?.IsEnabled,
                    policy?.DailyLimitMinutes,
                    evaluation.UsedMinutes,
                    remainingMinutes,
                    usageResponse is not null,
                    shouldLock);

                if (shouldLock)
                {
                    logger.LogWarning(
                        "Lock requested for local user {LocalUser} because remaining minutes are {RemainingMinutes}",
                        mapping.LocalUser,
                        remainingMinutes);

                    try
                    {
                        await sessionController.LockAsync(mapping, cancellationToken);
                        isLocked = true;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Lock failed for local user {LocalUser}", mapping.LocalUser);
                        message = $"Lock failed: {ex.Message}";
                    }
                }

                var snapshot = new AgentStatusSnapshot(
                    options.AgentId,
                    options.AgentVersion,
                    mapping.LocalUser,
                    mapping.ChildId,
                    response.Policy is not null,
                    response.IsFromCache,
                    isLocked,
                    remainingMinutes,
                    evaluation.UsedMinutes,
                    now,
                    response.Policy is null ? "No valid policy available." : message);

                snapshots.Add(snapshot);
                await agentStatusStore.SaveAsync(snapshot, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Agent cycle failed for local user {LocalUser}", mapping.LocalUser);
                var failed = new AgentStatusSnapshot(
                    options.AgentId,
                    options.AgentVersion,
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
