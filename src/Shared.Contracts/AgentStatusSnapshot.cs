namespace Shared.Contracts;

public sealed record AgentStatusSnapshot(
    string AgentId,
    string AgentVersion,
    string LocalUser,
    string ChildId,
    bool PolicyAvailable,
    bool IsOfflineMode,
    bool IsLocked,
    int? RemainingMinutes,
    int UsedMinutes,
    DateTimeOffset RecordedAtUtc,
    string? Message);
