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
            var directoryExists = Directory.Exists(directory);
            Directory.CreateDirectory(directory);
            if (!directoryExists)
            {
                SetUnixMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }

        var fileExists = File.Exists(statusFilePath);
        var tempFile = $"{statusFilePath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempFile))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, SessionGuardJsonContext.Default.AgentStatusSnapshot, cancellationToken);
        }

        File.Move(tempFile, statusFilePath, overwrite: true);
        if (!fileExists)
        {
            SetUnixMode(statusFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }

    private static void SetUnixMode(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }
}
