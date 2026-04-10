using Agent.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agent.Linux;

public sealed class AgentWorker(
    AgentCoordinator agentCoordinator,
    IOptions<AgentOptions> agentOptions,
    IOptions<AgentLinuxOptions> linuxOptions,
    ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var agent = agentOptions.Value;
        var linux = linuxOptions.Value;
        var interval = TimeSpan.FromSeconds(Math.Max(10, agent.PollIntervalSeconds));
        logger.LogInformation(
            "SessionGuard agent {AgentId} version {AgentVersion} started with poll interval {IntervalSeconds}s; cache={CacheDirectory}; status={StatusFilePath}; userMap={UserMapPath}",
            agent.AgentId,
            agent.AgentVersion,
            interval.TotalSeconds,
            linux.CacheDirectory,
            linux.StatusFilePath,
            linux.UserMapPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            await agentCoordinator.RunOnceAsync(stoppingToken);
            await Task.Delay(interval, stoppingToken);
        }
    }
}
