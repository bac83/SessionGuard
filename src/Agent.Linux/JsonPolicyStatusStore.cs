using System.Text.Json;
using Agent.Core;
using Shared.Contracts;

namespace Agent.Linux;

public sealed class JsonPolicyStatusStore(string statusFilePath) : IAgentStatusStore
{
    public async Task SaveAsync(AgentStatusSnapshot snapshot, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(statusFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempFile = $"{statusFilePath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempFile))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, SessionGuardJsonContext.Default.AgentStatusSnapshot, cancellationToken);
        }

        File.Move(tempFile, statusFilePath, overwrite: true);
    }
}
