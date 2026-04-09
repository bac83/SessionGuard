using Shared.Contracts;

namespace Agent.Core;

public sealed class PolicyEvaluator
{
    public PolicyEvaluationResult Evaluate(ChildPolicy? policy, int usedMinutes)
    {
        if (policy is null)
        {
            return new PolicyEvaluationResult(usedMinutes, null, false);
        }

        if (!policy.IsEnabled)
        {
            return new PolicyEvaluationResult(usedMinutes, 0, true);
        }

        var remaining = Math.Max(policy.DailyLimitMinutes - usedMinutes, 0);
        return new PolicyEvaluationResult(usedMinutes, remaining, remaining == 0);
    }
}

public sealed record PolicyEvaluationResult(
    int UsedMinutes,
    int? RemainingMinutes,
    bool ShouldLock);
