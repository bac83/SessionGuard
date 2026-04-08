namespace Shared.Contracts;

public sealed record AgentStatusSummary(
    string AgentId,
    string Hostname,
    string? LocalUser,
    string? ChildId,
    string? LastPolicyVersion,
    int UsedMinutesToday,
    int? RemainingMinutes,
    bool IsSessionLocked,
    bool IsOnline,
    DateTimeOffset? LastSeenAtUtc,
    DateTimeOffset? LastUsageReportAtUtc);
