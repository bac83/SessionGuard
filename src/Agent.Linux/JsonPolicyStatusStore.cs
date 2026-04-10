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
                SetUnixMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            }
        }

        var fileExists = File.Exists(statusFilePath);
        var fileMode = fileExists ? GetUnixMode(statusFilePath) : null;
        var tempFile = $"{statusFilePath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempFile))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, SessionGuardJsonContext.Default.AgentStatusSnapshot, cancellationToken);
        }

        if (fileMode is not null)
        {
            SetUnixMode(tempFile, fileMode.Value);
        }
        else
        {
            SetUnixMode(tempFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }

        File.Move(tempFile, statusFilePath, overwrite: true);
    }

    private static void SetUnixMode(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }

    private static UnixFileMode? GetUnixMode(string path)
    {
        return OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(path);
    }
}
