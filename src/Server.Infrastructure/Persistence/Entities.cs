namespace Server.Infrastructure.Persistence;

public sealed class ChildEntity
{
    public string ChildId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int DailyLimitMinutes { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ICollection<AgentEntity> Agents { get; set; } = [];
}

public sealed class AgentEntity
{
    public string AgentId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string? LocalUser { get; set; }
    public string? ChildId { get; set; }
    public string? AgentVersion { get; set; }
    public string? LastPolicyVersion { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public DateTimeOffset? LastUsageReportAtUtc { get; set; }
    public ChildEntity? Child { get; set; }
    public ICollection<UsageReportEntity> UsageReports { get; set; } = [];
}

public sealed class UsageReportEntity
{
    public int Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string ChildId { get; set; } = string.Empty;
    public string LocalUser { get; set; } = string.Empty;
    public DateOnly UsageDateUtc { get; set; }
    public int UsedMinutes { get; set; }
    public DateTimeOffset ReportedAtUtc { get; set; }
    public AgentEntity? Agent { get; set; }
}
