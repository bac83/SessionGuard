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

        var service = new LinuxSessionLockService(commandRunner, environmentReader, NullLogger<LinuxSessionLockService>.Instance);

        await service.LockAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        await commandRunner.Received(1).RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>());
        await commandRunner.DidNotReceive().RunAsync(
            "runuser",
            Arg.Any<string>(),
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
    public async Task LinuxSessionLockService_IncludesLockExitCodeWhenFallbackFails()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns((string?)null);
        commandRunner.RunAsync("loginctl", "list-sessions --no-legend", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "42 1001 alice seat0 tty2", string.Empty));
        commandRunner.RunAsync(
                "loginctl",
                "show-session 42 --property=Id --property=User --property=Display",
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, string.Empty));
        commandRunner.RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, string.Empty));
        commandRunner.RunAsync(
                "runuser",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
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
                "loginctl",
                "show-session 42 --property=Id --property=User --property=Display",
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "Id=42\nUser=1001\nDisplay=:0", string.Empty));
        var cinnamonArgs = BuildRunUserArgs("alice", "1001", ":0", "cinnamon-screensaver-command --lock");
        commandRunner.RunAsync("runuser", cinnamonArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));

        var service = new LinuxSessionLockService(commandRunner, environmentReader, NullLogger<LinuxSessionLockService>.Instance);

        await service.LockAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        await commandRunner.Received(1).RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>());
        await commandRunner.Received(1).RunAsync("runuser", cinnamonArgs, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinuxSessionLockService_UsesResolvedXdgSessionDetailsForDesktopFallback()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns("42");
        commandRunner.RunAsync("id", "-u -- 'alice'", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "1001", string.Empty));
        commandRunner.RunAsync(
                "loginctl",
                "show-session 42 --property=Id --property=User --property=Display",
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "Id=42\nUser=1001\nDisplay=:1", string.Empty));
        commandRunner.RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "logind did not lock"));
        var cinnamonArgs = BuildRunUserArgs("alice", "1001", ":1", "cinnamon-screensaver-command --lock");
        commandRunner.RunAsync("runuser", cinnamonArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));

        var service = new LinuxSessionLockService(commandRunner, environmentReader, NullLogger<LinuxSessionLockService>.Instance);

        await service.LockAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        await commandRunner.Received(1).RunAsync(
            "loginctl",
            "show-session 42 --property=Id --property=User --property=Display",
            Arg.Any<CancellationToken>());
        await commandRunner.Received(1).RunAsync("runuser", cinnamonArgs, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinuxSessionLockService_IgnoresXdgSessionWhenItBelongsToDifferentUser()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns("42");
        commandRunner.RunAsync("id", "-u -- 'alice'", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "1001", string.Empty));
        commandRunner.RunAsync(
                "loginctl",
                "show-session 42 --property=Id --property=User --property=Display",
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "Id=42\nUser=1002\nDisplay=:1", string.Empty));
        commandRunner.RunAsync("loginctl", "list-sessions --no-legend", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "43 1001 alice seat0 tty2", string.Empty));
        commandRunner.RunAsync("loginctl", "lock-session 43", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));

        var service = new LinuxSessionLockService(commandRunner, environmentReader, NullLogger<LinuxSessionLockService>.Instance);

        await service.LockAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        await commandRunner.DidNotReceive().RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>());
        await commandRunner.Received(1).RunAsync("loginctl", "lock-session 43", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinuxSessionLockService_TriesGnomeFallbackAfterCinnamonFailure()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns((string?)null);
        commandRunner.RunAsync("loginctl", "list-sessions --no-legend", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "42 1001 alice seat0 tty2", string.Empty));
        commandRunner.RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "logind did not lock"));
        commandRunner.RunAsync(
                "loginctl",
                "show-session 42 --property=Id --property=User --property=Display",
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "Id=42\nUser=1001\nDisplay=:2", string.Empty));
        var cinnamonArgs = BuildRunUserArgs("alice", "1001", ":2", "cinnamon-screensaver-command --lock");
        var gnomeArgs = BuildRunUserArgs("alice", "1001", ":2", "gnome-screensaver-command -l");
        commandRunner.RunAsync("runuser", cinnamonArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "cinnamon missing"));
        commandRunner.RunAsync("runuser", gnomeArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));

        var service = new LinuxSessionLockService(commandRunner, environmentReader, NullLogger<LinuxSessionLockService>.Instance);

        await service.LockAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        Received.InOrder(() =>
        {
            commandRunner.RunAsync("runuser", cinnamonArgs, Arg.Any<CancellationToken>());
            commandRunner.RunAsync("runuser", gnomeArgs, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task LinuxSessionLockService_UsesFreedesktopFallbackAfterCinnamonAndGnomeFail()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns((string?)null);
        commandRunner.RunAsync("loginctl", "list-sessions --no-legend", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "42 1001 alice seat0 tty2", string.Empty));
        commandRunner.RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "logind did not lock"));
        commandRunner.RunAsync(
                "loginctl",
                "show-session 42 --property=Id --property=User --property=Display",
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "Id=42\nUser=1001\nDisplay=:0", string.Empty));
        var cinnamonArgs = BuildRunUserArgs("alice", "1001", ":0", "cinnamon-screensaver-command --lock");
        var gnomeArgs = BuildRunUserArgs("alice", "1001", ":0", "gnome-screensaver-command -l");
        var dbusArgs = BuildRunUserArgs("alice", "1001", ":0", "dbus-send --session --dest=org.freedesktop.ScreenSaver --type=method_call /ScreenSaver org.freedesktop.ScreenSaver.Lock");
        commandRunner.RunAsync("runuser", cinnamonArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "missing"));
        commandRunner.RunAsync("runuser", gnomeArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "missing"));
        commandRunner.RunAsync("runuser", dbusArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));

        var service = new LinuxSessionLockService(commandRunner, environmentReader, NullLogger<LinuxSessionLockService>.Instance);

        await service.LockAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        await commandRunner.Received(1).RunAsync("runuser", dbusArgs, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinuxSessionLockService_FallsThroughToXflock4WhenScreensaversFail()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns((string?)null);
        commandRunner.RunAsync("loginctl", "list-sessions --no-legend", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "42 1001 alice seat0 tty2", string.Empty));
        commandRunner.RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "logind did not lock"));
        commandRunner.RunAsync(
                "loginctl",
                "show-session 42 --property=Id --property=User --property=Display",
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "Id=42\nUser=1001\nDisplay=:0", string.Empty));
        var cinnamonArgs = BuildRunUserArgs("alice", "1001", ":0", "cinnamon-screensaver-command --lock");
        var gnomeArgs = BuildRunUserArgs("alice", "1001", ":0", "gnome-screensaver-command -l");
        var dbusArgs = BuildRunUserArgs("alice", "1001", ":0", "dbus-send --session --dest=org.freedesktop.ScreenSaver --type=method_call /ScreenSaver org.freedesktop.ScreenSaver.Lock");
        var xflock4Args = BuildRunUserArgs("alice", "1001", ":0", "xflock4");
        commandRunner.RunAsync("runuser", cinnamonArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "missing"));
        commandRunner.RunAsync("runuser", gnomeArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "missing"));
        commandRunner.RunAsync("runuser", dbusArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "missing"));
        commandRunner.RunAsync("runuser", xflock4Args, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));

        var service = new LinuxSessionLockService(commandRunner, environmentReader, NullLogger<LinuxSessionLockService>.Instance);

        await service.LockAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        await commandRunner.Received(1).RunAsync("runuser", xflock4Args, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinuxSessionLockService_FallsThroughToXdgScreensaverAsLastResort()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns((string?)null);
        commandRunner.RunAsync("loginctl", "list-sessions --no-legend", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "42 1001 alice seat0 tty2", string.Empty));
        commandRunner.RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "logind did not lock"));
        commandRunner.RunAsync(
                "loginctl",
                "show-session 42 --property=Id --property=User --property=Display",
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "Id=42\nUser=1001\nDisplay=:0", string.Empty));
        var cinnamonArgs = BuildRunUserArgs("alice", "1001", ":0", "cinnamon-screensaver-command --lock");
        var gnomeArgs = BuildRunUserArgs("alice", "1001", ":0", "gnome-screensaver-command -l");
        var dbusArgs = BuildRunUserArgs("alice", "1001", ":0", "dbus-send --session --dest=org.freedesktop.ScreenSaver --type=method_call /ScreenSaver org.freedesktop.ScreenSaver.Lock");
        var xflock4Args = BuildRunUserArgs("alice", "1001", ":0", "xflock4");
        var xdgArgs = BuildRunUserArgs("alice", "1001", ":0", "xdg-screensaver lock");
        commandRunner.RunAsync("runuser", cinnamonArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "missing"));
        commandRunner.RunAsync("runuser", gnomeArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "missing"));
        commandRunner.RunAsync("runuser", dbusArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "missing"));
        commandRunner.RunAsync("runuser", xflock4Args, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "missing"));
        commandRunner.RunAsync("runuser", xdgArgs, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));

        var service = new LinuxSessionLockService(commandRunner, environmentReader, NullLogger<LinuxSessionLockService>.Instance);

        await service.LockAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        Received.InOrder(() =>
        {
            commandRunner.RunAsync("runuser", cinnamonArgs, Arg.Any<CancellationToken>());
            commandRunner.RunAsync("runuser", gnomeArgs, Arg.Any<CancellationToken>());
            commandRunner.RunAsync("runuser", dbusArgs, Arg.Any<CancellationToken>());
            commandRunner.RunAsync("runuser", xflock4Args, Arg.Any<CancellationToken>());
            commandRunner.RunAsync("runuser", xdgArgs, Arg.Any<CancellationToken>());
        });
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

        var firstTracker = new LocalUsageTracker(firstTimeProvider, AlwaysActiveDetector(), options, NullLogger<LocalUsageTracker>.Instance);
        Assert.Equal(0, await firstTracker.GetUsedMinutesAsync(mapping, day, CancellationToken.None));
        Assert.Equal(5, await firstTracker.GetUsedMinutesAsync(mapping, day, CancellationToken.None));

        var secondTracker = new LocalUsageTracker(secondTimeProvider, AlwaysActiveDetector(), options, NullLogger<LocalUsageTracker>.Instance);
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

        var tracker = new LocalUsageTracker(timeProvider, AlwaysActiveDetector(), options, NullLogger<LocalUsageTracker>.Instance);
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
        var tracker = new LocalUsageTracker(timeProvider, AlwaysActiveDetector(), options, NullLogger<LocalUsageTracker>.Instance);

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
        var tracker = new LocalUsageTracker(timeProvider, AlwaysActiveDetector(), options, NullLogger<LocalUsageTracker>.Instance);

        Assert.Equal(0, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None));
        var persistedAfterFirstPoll = await ReadPersistedLastSeenAsync(directory);

        Assert.Equal(0, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None));
        var persistedAfterSecondPoll = await ReadPersistedLastSeenAsync(directory);

        Assert.Equal(persistedAfterFirstPoll, persistedAfterSecondPoll);
    }

    [Fact]
    public async Task LocalUsageTracker_FreezesUsageWhileIdle()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sessionguard-usage-tests", Guid.NewGuid().ToString("N"));
        var options = new AgentLinuxOptions { CacheDirectory = directory };
        var mapping = new UserChildMapping("alice", "child-01");
        var timeProvider = new SequenceTimeProvider(
            new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 12, 5, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 12, 30, 0, TimeSpan.Zero));
        var idleDetector = new ScriptedIdleDetector(true, true, false);

        var tracker = new LocalUsageTracker(timeProvider, idleDetector, options, NullLogger<LocalUsageTracker>.Instance);
        Assert.Equal(0, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None));
        Assert.Equal(5, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None));
        Assert.Equal(5, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None));
    }

    [Fact]
    public async Task LocalUsageTracker_DoesNotBackfillIdleGapWhenActiveResumes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sessionguard-usage-tests", Guid.NewGuid().ToString("N"));
        var options = new AgentLinuxOptions { CacheDirectory = directory };
        var mapping = new UserChildMapping("alice", "child-01");
        var timeProvider = new SequenceTimeProvider(
            new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 12, 5, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 12, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 12, 32, 0, TimeSpan.Zero));
        var idleDetector = new ScriptedIdleDetector(true, true, false, true);

        var tracker = new LocalUsageTracker(timeProvider, idleDetector, options, NullLogger<LocalUsageTracker>.Instance);
        Assert.Equal(0, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None));
        Assert.Equal(5, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None));
        Assert.Equal(5, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None));
        Assert.Equal(7, await tracker.GetUsedMinutesAsync(mapping, new DateOnly(2026, 4, 8), CancellationToken.None));
    }

    [Fact]
    public async Task LinuxIdleDetector_ReportsInactiveWhenLockedHintIsYes()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns("42");
        commandRunner.RunAsync("id", "-u -- 'alice'", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "1001", string.Empty));
        commandRunner.RunAsync(
                "loginctl",
                "show-session 42 --property=LockedHint --property=IdleHint --property=Display --property=User",
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "LockedHint=yes\nIdleHint=no\nDisplay=:0\nUser=1001", string.Empty));

        var detector = new LinuxIdleDetector(commandRunner, environmentReader, new AgentLinuxOptions(), NullLogger<LinuxIdleDetector>.Instance);

        var active = await detector.IsActiveAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        Assert.False(active);
    }

    [Fact]
    public async Task LinuxIdleDetector_ReportsActiveWhenLogindHintsAreClear()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns("42");
        commandRunner.RunAsync("id", "-u -- 'alice'", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "1001", string.Empty));
        commandRunner.RunAsync(
                "loginctl",
                "show-session 42 --property=LockedHint --property=IdleHint --property=Display --property=User",
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "LockedHint=no\nIdleHint=no\nDisplay=:0\nUser=1001", string.Empty));
        commandRunner.RunAsync(
                "runuser",
                Arg.Is<string>(arguments => arguments.Contains("xprintidle", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "1500", string.Empty));

        var detector = new LinuxIdleDetector(commandRunner, environmentReader, new AgentLinuxOptions(), NullLogger<LinuxIdleDetector>.Instance);

        var active = await detector.IsActiveAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        Assert.True(active);
    }

    [Fact]
    public async Task LinuxIdleDetector_ReportsInactiveWhenXprintIdleExceedsThreshold()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns("42");
        commandRunner.RunAsync("id", "-u -- 'alice'", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "1001", string.Empty));
        commandRunner.RunAsync(
                "loginctl",
                "show-session 42 --property=LockedHint --property=IdleHint --property=Display --property=User",
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "LockedHint=no\nIdleHint=no\nDisplay=:0\nUser=1001", string.Empty));
        commandRunner.RunAsync(
                "runuser",
                Arg.Is<string>(arguments => arguments.Contains("xprintidle", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "600000", string.Empty));

        var detector = new LinuxIdleDetector(commandRunner, environmentReader, new AgentLinuxOptions { IdleThresholdSeconds = 300 }, NullLogger<LinuxIdleDetector>.Instance);

        var active = await detector.IsActiveAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        Assert.False(active);
    }

    [Fact]
    public async Task LinuxIdleDetector_FailsOpenWhenLoginctlIsUnavailable()
    {
        var commandRunner = Substitute.For<ICommandRunner>();
        var environmentReader = Substitute.For<IEnvironmentReader>();
        environmentReader.GetEnvironmentVariable("XDG_SESSION_ID").Returns((string?)null);
        commandRunner.RunAsync("loginctl", "list-sessions --no-legend", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(127, string.Empty, "command not found"));

        var detector = new LinuxIdleDetector(commandRunner, environmentReader, new AgentLinuxOptions(), NullLogger<LinuxIdleDetector>.Instance);

        var active = await detector.IsActiveAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        Assert.True(active);
    }

    private static string BuildRunUserArgs(string localUser, string uid, string display, string command)
    {
        var environment = $"DISPLAY={display} XDG_RUNTIME_DIR=/run/user/{uid} DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/{uid}/bus";
        return $"-u {localUser} -- env {environment} {command}";
    }

    private static IIdleDetector AlwaysActiveDetector() => new ScriptedIdleDetector(true);

    private sealed class ScriptedIdleDetector(params bool[] sequence) : IIdleDetector
    {
        private readonly Queue<bool> _values = new(sequence);
        private bool _last = sequence.Length > 0 ? sequence[^1] : true;

        public Task<bool> IsActiveAsync(UserChildMapping mapping, CancellationToken cancellationToken)
        {
            if (_values.Count > 0)
            {
                _last = _values.Dequeue();
            }

            return Task.FromResult(_last);
        }
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
