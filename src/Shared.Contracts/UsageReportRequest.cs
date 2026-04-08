namespace Shared.Contracts;

public sealed record UsageReportRequest(
    string AgentId,
    string ChildId,
    string LocalUser,
    DateOnly UsageDateUtc,
    int UsedMinutes,
    DateTimeOffset ReportedAtUtc);
