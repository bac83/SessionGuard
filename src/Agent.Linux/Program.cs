using Agent.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace Agent.Linux;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddOptions<AgentOptions>()
            .Bind(builder.Configuration.GetSection("SessionGuard:Agent"))
            .PostConfigure(options =>
            {
                options.AgentId = string.IsNullOrWhiteSpace(options.AgentId)
                    ? Environment.MachineName.ToLowerInvariant()
                    : options.AgentId.Trim();
                options.AgentVersion = string.IsNullOrWhiteSpace(options.AgentVersion)
                    ? ResolveAgentVersion()
                    : options.AgentVersion.Trim();
                options.PollIntervalSeconds = options.PollIntervalSeconds <= 0 ? 60 : options.PollIntervalSeconds;
            });

        builder.Services.AddOptions<AgentLinuxOptions>()
            .Bind(builder.Configuration.GetSection(AgentLinuxOptions.SectionName));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<PolicyEvaluator>();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AgentOptions>>().Value);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AgentLinuxOptions>>().Value);
        builder.Services.AddHttpClient<IPolicyClient, HttpPolicyClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<AgentLinuxOptions>();
            client.BaseAddress = new Uri(options.ServerBaseUrl);
        });
        builder.Services.AddSingleton<IPolicyCache>(sp =>
        {
            var options = sp.GetRequiredService<AgentLinuxOptions>();
            return new FilePolicyCache(options.CacheDirectory);
        });
        builder.Services.AddSingleton<IUserMappingProvider>(sp =>
        {
            var options = sp.GetRequiredService<AgentLinuxOptions>();
            return new JsonUserMappingProvider(options.UserMapPath);
        });
        builder.Services.AddSingleton<IUsageTracker, LocalUsageTracker>();
        builder.Services.AddSingleton<ISessionController, LinuxSessionLockService>();
        builder.Services.AddSingleton<IAgentStatusStore>(sp =>
        {
            var options = sp.GetRequiredService<AgentLinuxOptions>();
            return new JsonPolicyStatusStore(options.StatusFilePath);
        });
        builder.Services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
        builder.Services.AddSingleton<IEnvironmentReader, SystemEnvironmentReader>();
        builder.Services.AddSingleton<AgentCoordinator>();
        builder.Services.AddHostedService<AgentWorker>();

        await builder.Build().RunAsync();
    }

    private static string ResolveAgentVersion()
    {
        var assembly = typeof(Program).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }
}
