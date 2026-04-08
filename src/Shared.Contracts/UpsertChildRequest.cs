namespace Shared.Contracts;

public sealed record UpsertChildRequest(
    string ChildId,
    string DisplayName,
    int DailyLimitMinutes,
    bool IsEnabled);
