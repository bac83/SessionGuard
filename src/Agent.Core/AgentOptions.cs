namespace Agent.Core;

public sealed class AgentOptions
{
    public string AgentId { get; set; } = Environment.MachineName.ToLowerInvariant();
    public string AgentVersion { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; } = 60;
}
