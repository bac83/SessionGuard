using System.Globalization;
using System.Text.Json.Serialization;

namespace Shared.Contracts;

[method: JsonConstructor]
public sealed partial record ChildPolicy(
    string ChildId,
    int DailyLimitMinutes,
    bool IsEnabled,
    string Version,
    DateOnly EffectiveDateUtc,
    DateTimeOffset UpdatedAtUtc);

public partial record ChildPolicy
{
    public ChildPolicy(
        string childId,
        int dailyLimitMinutes,
        bool isEnabled,
        int version,
        DateTimeOffset effectiveDateUtc,
        DateTimeOffset updatedAtUtc)
        : this(
            childId,
            dailyLimitMinutes,
            isEnabled,
            version.ToString(CultureInfo.InvariantCulture),
            DateOnly.FromDateTime(effectiveDateUtc.UtcDateTime),
            updatedAtUtc)
    {
    }
}
