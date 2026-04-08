namespace Shared.Contracts;

public sealed record ChildSummary(
    string ChildId,
    string DisplayName,
    int DailyLimitMinutes,
    bool IsEnabled,
    int CurrentUsageMinutes,
    int RemainingMinutes,
    DateTimeOffset UpdatedAtUtc);
