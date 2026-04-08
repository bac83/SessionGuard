namespace Agent.Linux;

public sealed class AgentLinuxOptions
{
    public const string SectionName = "SessionGuard:Agent";
    public string ServerBaseUrl { get; set; } = "http://localhost:8080";
    public string UserMapPath { get; set; } = "/etc/sessionguard/user-map.json";
    public string CacheDirectory { get; set; } = "/var/lib/sessionguard/cache";
    public string StatusFilePath { get; set; } = "/var/lib/sessionguard/status/agent-status.json";
}
