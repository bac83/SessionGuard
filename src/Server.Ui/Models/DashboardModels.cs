using System.ComponentModel.DataAnnotations;

namespace Server.Ui.Models;

public sealed record ChildProfile(
    string ChildId,
    string DisplayName,
    int DailyBudgetMinutes,
    bool IsActive,
    int UsedTodayMinutes,
    int RemainingMinutes,
    DateTimeOffset UpdatedAtUtc);

public sealed record AgentStatus(
    string AgentId,
    string AgentVersion,
    string HostName,
    string LinuxUserName,
    string ChildId,
    bool IsOnline,
    bool IsSessionLocked,
    int UsedTodayMinutes,
    int? RemainingMinutes,
    DateTimeOffset? LastSeenAtUtc);

public sealed record DashboardSnapshot(
    IReadOnlyList<ChildProfile> Children,
    IReadOnlyList<AgentStatus> Agents,
    DateTimeOffset GeneratedAtUtc);

public sealed class ChildProfileDraft
{
    [Required]
    [StringLength(32, MinimumLength = 2)]
    [RegularExpression("^[a-z0-9][a-z0-9-]*$", ErrorMessage = "Use lowercase letters, digits, or hyphens.")]
    public string ChildId { get; set; } = string.Empty;

    [Required]
    [StringLength(64, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [Range(15, 1440, ErrorMessage = "Daily budget must be between 15 and 1440 minutes.")]
    public int DailyBudgetMinutes { get; set; } = 120;

    public bool IsActive { get; set; } = true;
}
