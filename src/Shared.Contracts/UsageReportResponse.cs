namespace Shared.Contracts;

public sealed record UsageReportResponse(
    string AgentId,
    string ChildId,
    int UsedMinutes,
    int? RemainingMinutes,
    DateTimeOffset AcceptedAtUtc);
