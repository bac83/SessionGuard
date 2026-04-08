using System.Text.Json.Serialization;

namespace Shared.Contracts;

public sealed partial record PolicyFetchResponse(
    string AgentId,
    string? ChildId,
    ChildPolicy? Policy,
    DateTimeOffset IssuedAtUtc,
    bool IsFromCache = false);

public sealed partial record PolicyFetchResponse
{
    [JsonIgnore]
    public string? ErrorMessage { get; init; }

    [JsonIgnore]
    public PolicySnapshot? SnapshotOverride { get; init; }

    [JsonIgnore]
    public PolicySnapshot? Snapshot => SnapshotOverride ?? Policy?.ToPolicySnapshot(IssuedAtUtc, IsFromCache);

    [JsonIgnore]
    public bool HasSnapshot => Snapshot is not null;

    public static PolicyFetchResponse Success(PolicySnapshot snapshot)
    {
        return new PolicyFetchResponse(
                snapshot.ChildId,
                snapshot.ChildId,
                snapshot.ToChildPolicy(),
                snapshot.CachedAtUtc,
                false)
        {
            SnapshotOverride = snapshot
        };
    }

    public static PolicyFetchResponse Failure(string childId, string errorMessage)
    {
        return new PolicyFetchResponse(childId, childId, null, DateTimeOffset.MinValue, false)
        {
            ErrorMessage = errorMessage
        };
    }
}

internal static class PolicyFetchResponseExtensions
{
    public static PolicySnapshot ToPolicySnapshot(this ChildPolicy policy, DateTimeOffset issuedAtUtc, bool isFromCache)
    {
        var version = int.TryParse(policy.Version, out var parsedVersion) ? parsedVersion : 0;
        return new PolicySnapshot(
            policy.ChildId,
            policy.DailyLimitMinutes,
            policy.IsEnabled,
            version,
            policy.EffectiveDateUtc.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            issuedAtUtc.AddDays(1),
            issuedAtUtc,
            isFromCache ? "cache" : "server");
    }
}
