using System.Text.Json;
using Agent.Linux;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Contracts;

namespace Agent.Core.Tests;

public sealed class AgentLinuxTests
{
    [Fact]
    public async Task ProcessCommandRunner_CapturesStdoutAndStderr()
    {
        var runner = new ProcessCommandRunner();
        var shell = OperatingSystem.IsWindows() ? "powershell" : "pwsh";

        var result = await runner.RunAsync(
            shell,
            "-NoProfile -Command \"[Console]::Out.WriteLine('out'); [Console]::Error.WriteLine('err')\"",
            CancellationToken.None);

        Assert.Contains("out", result.StandardOutput);
        Assert.Contains("err", result.StandardError);
    }

    [Fact]
    public async Task JsonUserMappingProvider_LoadsAllMappingsFromJson()
    {
        var path = Path.Combine(Path.GetTempPath(), "sessionguard-agent-tests", Guid.NewGuid().ToString("N"), "user-map.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["alice"] = "child-01",
            ["bob"] = "child-02"
        }));

        var provider = new JsonUserMappingProvider(path);

        var mappings = await provider.GetMappingsAsync(CancellationToken.None);

        Assert.Equal(2, mappings.Count);
        Assert.Contains(mappings, mapping => mapping.LocalUser == "alice" && mapping.ChildId == "child-01");
        Assert.Contains(mappings, mapping => mapping.LocalUser == "bob" && mapping.ChildId == "child-02");
    }

    [Fact]
    public async Task LinuxSessionLockService_ResolvesSessionsWhenXdgSessionIsMissing()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns((string?)null);
        commandRunner.RunAsync("loginctl", "list-sessions --no-legend", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "42 1001 alice seat0 tty2\n43 1002 bob seat0 tty3", string.Empty));
        commandRunner.RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        commandRunner.RunAsync(
                "runuser",
                Arg.Is<string>(arguments => arguments.Contains("alice", StringComparison.Ordinal)
                    && arguments.Contains("cinnamon-screensaver-command --lock", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));

        var service = new LinuxSessionLockService(commandRunner, environmentReader, NullLogger<LinuxSessionLockService>.Instance);

        await service.LockAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        await commandRunner.Received(1).RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>());
        await commandRunner.Received(1).RunAsync(
            "runuser",
            Arg.Is<string>(arguments => arguments.Contains("XDG_RUNTIME_DIR=/run/user/1001", StringComparison.Ordinal)
                && arguments.Contains("DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1001/bus", StringComparison.Ordinal)
                && arguments.Contains("cinnamon-screensaver-command --lock", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinuxSessionLockService_IncludesExitCodeWhenSessionListingFailsWithoutMessage()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns((string?)null);
        commandRunner.RunAsync("loginctl", "list-sessions --no-legend", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(5, string.Empty, string.Empty));

        var service = new LinuxSessionLockService(commandRunner, environmentReader, NullLogger<LinuxSessionLockService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LockAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None));

        Assert.Contains("exit code 5", exception.Message);
    }

    [Fact]
    public async Task LinuxSessionLockService_IncludesFallbackDetailsWhenLockFails()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns("42");
        commandRunner.RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, string.Empty));

        var service = new LinuxSessionLockService(commandRunner, environmentReader, NullLogger<LinuxSessionLockService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LockAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None));

        Assert.Contains("exit code 1", exception.Message);
    }

    [Fact]
    public async Task LinuxSessionLockService_UsesDesktopFallbackWhenLoginctlLockFails()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns((string?)null);
        commandRunner.RunAsync("loginctl", "list-sessions --no-legend", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "42 1001 alice seat0 tty2", string.Empty));
        commandRunner.RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "logind did not lock"));
        commandRunner.RunAsync(
                "runuser",
                Arg.Is<string>(arguments => arguments.Contains("alice", StringComparison.Ordinal)
                    && arguments.Contains("cinnamon-screensaver-command --lock", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));

        var service = new LinuxSessionLockService(commandRunner, environmentReader, NullLogger<LinuxSessionLockService>.Instance);

        await service.LockAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        await commandRunner.Received(1).RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>());
        await commandRunner.Received(1).RunAsync(
            "runuser",
            Arg.Is<string>(arguments => arguments.Contains("cinnamon-screensaver-command --lock", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JsonPolicyStatusStore_OverwritesExistingSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sessionguard-status-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "status.json");
        var store = new JsonPolicyStatusStore(path);

        var first = new AgentStatusSnapshot(
            "agent-01",
            "1.2.3",
            "alice",
            "child-01",
            true,
            false,
            false,
            80,
            10,
            DateTimeOffset.UtcNow,
            "v1");
        var second = first with { UsedMinutes = 25, RemainingMinutes = 65, Message = "v2" };

        await store.SaveAsync(first, CancellationToken.None);
        await store.SaveAsync(second, CancellationToken.None);

        var json = await File.ReadAllTextAsync(path);
        var saved = JsonSerializer.Deserialize(json, SessionGuardJsonContext.Default.AgentStatusSnapshot);
        Assert.NotNull(saved);
        Assert.Equal("1.2.3", saved!.AgentVersion);
        Assert.Equal(25, saved.UsedMinutes);
        Assert.Equal("v2", saved.Message);
    }

    [Fact]
    public async Task LocalUsageTracker_PersistsUsedMinutesAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sessionguard-usage-tests", Guid.NewGuid().ToString("N"));
        var options = new AgentLinuxOptions { CacheDirectory = directory };
        var mapping = new UserChildMapping("alice", "child-01");
        var day = new DateOnly(2026, 4, 8);
        var firstTimeProvider = new SequenceTimeProvider(
            new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 12, 5, 0, TimeSpan.Zero));
        var secondTimeProvider = new SequenceTimeProvider(
            new DateTimeOffset(2026, 4, 8, 12, 10, 0, TimeSpan.Zero));

        var firstTracker = new LocalUsageTracker(firstTimeProvider, options, NullLogger<LocalUsageTracker>.Instance);
        Assert.Equal(0, await firstTracker.GetUsedMinutesAsync(mapping, day, CancellationToken.None));
        Assert.Equal(5, await firstTracker.GetUsedMinutesAsync(mapping, day, CancellationToken.None));

        var secondTracker = new LocalUsageTracker(secondTimeProvider, options, NullLogger<LocalUsageTracker>.Instance);
        Assert.Equal(10, await secondTracker.GetUsedMinutesAsync(mapping, day, CancellationToken.None));
    }

    [Fact]
    public async Task LocalUsageTracker_ResetsWhenUsageDayChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sessionguard-usage-tests", Guid.NewGuid().ToString("N"));
        var options = new AgentLinuxOptions { CacheDirectory = directory };
        var mapping = new UserChildMapping("alice", "child-01");
        var timeProvider = new SequenceTimeProvider(
            new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 12, 5, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 9, 0, 1, 0, TimeSpan.Zero));

        var tracker = new LocalUsageTracker(timeProvider, options, NullLogger<LocalUsageTracker>.Instance);
        Assert.Equal(0, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None));
        Assert.Equal(5, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None));
        Assert.Equal(0, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 9), CancellationToken.None));
    }

    [Fact]
    public async Task LocalUsageTracker_TreatsCorruptedStateFileAsEmptyState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sessionguard-usage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "usage-state.json"), "{ not valid json");

        var options = new AgentLinuxOptions { CacheDirectory = directory };
        var mapping = new UserChildMapping("alice", "child-01");
        var timeProvider = new SequenceTimeProvider(new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero));
        var tracker = new LocalUsageTracker(timeProvider, options, NullLogger<LocalUsageTracker>.Instance);

        var used = await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None);

        Assert.Equal(0, used);
    }

    [Fact]
    public async Task LocalUsageTracker_DoesNotPersistWhenElapsedMinutesIsZero()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sessionguard-usage-tests", Guid.NewGuid().ToString("N"));
        var options = new AgentLinuxOptions { CacheDirectory = directory };
        var mapping = new UserChildMapping("alice", "child-01");
        var timeProvider = new SequenceTimeProvider(
            new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 12, 0, 30, TimeSpan.Zero));
        var tracker = new LocalUsageTracker(timeProvider, options, NullLogger<LocalUsageTracker>.Instance);

        Assert.Equal(0, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None));
        var persistedAfterFirstPoll = await ReadPersistedLastSeenAsync(directory);

        Assert.Equal(0, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None));
        var persistedAfterSecondPoll = await ReadPersistedLastSeenAsync(directory);

        Assert.Equal(persistedAfterFirstPoll, persistedAfterSecondPoll);
    }

    private static async Task<DateTimeOffset?> ReadPersistedLastSeenAsync(string directory)
    {
        var path = Path.Combine(directory, "usage-state.json");
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream);
        var rawValue = document.RootElement
            .GetProperty("users")
            .GetProperty("alice")
            .GetProperty("lastSeen")
            .GetString();

        return rawValue is null ? null : DateTimeOffset.Parse(rawValue);
    }

    private sealed class SequenceTimeProvider(params DateTimeOffset[] values) : TimeProvider
    {
        private readonly Queue<DateTimeOffset> _values = new(values);

        public override DateTimeOffset GetUtcNow()
        {
            if (_values.Count == 0)
            {
                throw new InvalidOperationException("No more timestamps available.");
            }

            return _values.Dequeue();
        }
    }
}
