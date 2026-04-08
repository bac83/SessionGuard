using Agent.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agent.Linux;

public sealed class AgentWorker(
    AgentCoordinator agentCoordinator,
    IOptions<AgentOptions> agentOptions,
    ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(10, agentOptions.Value.PollIntervalSeconds));
        logger.LogInformation("SessionGuard agent started with poll interval {IntervalSeconds}s", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await agentCoordinator.RunOnceAsync(stoppingToken);
            await Task.Delay(interval, stoppingToken);
        }
    }
}
