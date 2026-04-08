using System.Text.Json;
using Shared.Contracts;

var statusFilePath = Environment.GetEnvironmentVariable("SESSIONGUARD_STATUS_FILE")
    ?? "/var/lib/sessionguard/status/agent-status.json";

if (!File.Exists(statusFilePath))
{
    Console.Error.WriteLine($"No status file found at '{statusFilePath}'.");
    return 1;
}

await using var stream = File.OpenRead(statusFilePath);
var snapshot = await JsonSerializer.DeserializeAsync(stream, SessionGuardJsonContext.Default.AgentStatusSnapshot);

if (snapshot is null)
{
    Console.Error.WriteLine("The agent status file could not be parsed.");
    return 1;
}

Console.WriteLine("SessionGuard Status");
Console.WriteLine("-------------------");
Console.WriteLine($"Agent:      {snapshot.AgentId}");
Console.WriteLine($"User:       {snapshot.LocalUser}");
Console.WriteLine($"Child:      {snapshot.ChildId}");
Console.WriteLine($"Offline:    {(snapshot.IsOfflineMode ? "yes" : "no")}");
Console.WriteLine($"Locked:     {(snapshot.IsLocked ? "yes" : "no")}");
Console.WriteLine($"Used:       {snapshot.UsedMinutes} min");
Console.WriteLine($"Remaining:  {(snapshot.RemainingMinutes is null ? "n/a" : $"{snapshot.RemainingMinutes} min")}");
Console.WriteLine($"Updated:    {snapshot.RecordedAtUtc:O}");

if (!string.IsNullOrWhiteSpace(snapshot.Message))
{
    Console.WriteLine($"Message:    {snapshot.Message}");
}

return 0;
