namespace Shared.Contracts;

public sealed record PolicySnapshot(
    string ChildId,
    int DailyBudgetMinutes,
    bool IsEnabled,
    int Version,
    DateTimeOffset EffectiveDateUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CachedAtUtc,
    string Source)
{
    public ChildPolicy ToChildPolicy()
    {
        return new ChildPolicy(
            ChildId,
            DailyBudgetMinutes,
            IsEnabled,
            Version.ToString(),
            DateOnly.FromDateTime(EffectiveDateUtc.UtcDateTime),
            CachedAtUtc);
    }

    public bool IsUsable(DateTimeOffset utcNow)
    {
        return IsEnabled && utcNow <= ExpiresAtUtc;
    }
}
