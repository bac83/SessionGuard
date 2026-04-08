using Agent.Core;
using Shared.Contracts;

namespace Agent.Linux;

public sealed class LocalUsageTracker(TimeProvider timeProvider) : IUsageTracker
{
    private readonly Dictionary<string, (DateOnly Day, DateTimeOffset LastSeen, int UsedMinutes)> _state = new(StringComparer.OrdinalIgnoreCase);

    public Task<int> GetUsedMinutesAsync(UserChildMapping mapping, DateOnly usageDateUtc, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!_state.TryGetValue(mapping.LocalUser, out var current) || current.Day != usageDateUtc)
        {
            _state[mapping.LocalUser] = (usageDateUtc, now, 0);
            return Task.FromResult(0);
        }

        var elapsedMinutes = Math.Max((int)Math.Floor((now - current.LastSeen).TotalMinutes), 0);
        var used = current.UsedMinutes + elapsedMinutes;
        _state[mapping.LocalUser] = (usageDateUtc, now, used);
        return Task.FromResult(used);
    }
}
