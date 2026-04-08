using System.Text.Json;
using Agent.Linux;
using NSubstitute;
using Shared.Contracts;

namespace Agent.Core.Tests;

public sealed class AgentLinuxTests
{
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

        var service = new LinuxSessionLockService(commandRunner, environmentReader);

        await service.LockAsync(new UserChildMapping("alice", "child-01"), CancellationToken.None);

        await commandRunner.Received(1).RunAsync("loginctl", "lock-session 42", Arg.Any<CancellationToken>());
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
        Assert.Equal(25, saved!.UsedMinutes);
        Assert.Equal("v2", saved.Message);
    }
}
