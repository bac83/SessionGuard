using Agent.Core;
using Shared.Contracts;
using System.Text.Json;

namespace Agent.Linux;

public sealed class LocalUsageTracker : IUsageTracker
{
    private readonly TimeProvider _timeProvider;
    private readonly string _stateFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, UsageStateEntry> _state = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public LocalUsageTracker(TimeProvider timeProvider, AgentLinuxOptions options)
    {
        _timeProvider = timeProvider;
        _stateFilePath = Path.Combine(options.CacheDirectory, "usage-state.json");
    }

    public async Task<int> GetUsedMinutesAsync(UserChildMapping mapping, DateOnly usageDateUtc, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await EnsureLoadedAsync(cancellationToken);

            if (!_state.TryGetValue(mapping.LocalUser, out var current) || current.Day != usageDateUtc)
            {
                _state[mapping.LocalUser] = new UsageStateEntry(usageDateUtc, now, 0);
                await SaveAsync(cancellationToken);
                return 0;
            }

            var elapsedMinutes = Math.Max((int)Math.Floor((now - current.LastSeen).TotalMinutes), 0);
            var used = current.UsedMinutes + elapsedMinutes;
            _state[mapping.LocalUser] = new UsageStateEntry(usageDateUtc, now, used);
            await SaveAsync(cancellationToken);
            return used;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_stateFilePath))
        {
            return;
        }

        await using var stream = File.OpenRead(_stateFilePath);
        var file = await JsonSerializer.DeserializeAsync<UsageStateFile>(stream, _serializerOptions, cancellationToken);
        if (file?.Users is null)
        {
            return;
        }

        _state.Clear();
        foreach (var entry in file.Users)
        {
            _state[entry.Key] = entry.Value;
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_stateFilePath}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, new UsageStateFile(new Dictionary<string, UsageStateEntry>(_state, StringComparer.OrdinalIgnoreCase)), _serializerOptions, cancellationToken);
        }

        File.Move(tempPath, _stateFilePath, true);
    }
}
