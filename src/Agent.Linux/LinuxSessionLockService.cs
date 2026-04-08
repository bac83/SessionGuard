using Agent.Core;
using Shared.Contracts;

namespace Agent.Linux;

public sealed class LinuxSessionLockService(ICommandRunner commandRunner, IEnvironmentReader environmentReader) : ISessionController
{
    public async Task LockAsync(UserChildMapping mapping, CancellationToken cancellationToken)
    {
        var sessionIds = await ResolveSessionIdsAsync(mapping, cancellationToken);
        if (sessionIds.Count == 0)
        {
            throw new InvalidOperationException("Unable to resolve a login session to lock.");
        }

        foreach (var sessionId in sessionIds)
        {
            var result = await commandRunner.RunAsync("loginctl", $"lock-session {sessionId}", cancellationToken);
            if (!result.Succeeded)
            {
                var detail = FirstNonEmpty(result.StandardError, result.StandardOutput)
                    ?? $"loginctl lock-session {sessionId} failed with exit code {result.ExitCode}.";
                throw new InvalidOperationException(detail);
            }
        }
    }

    private async Task<List<string>> ResolveSessionIdsAsync(UserChildMapping mapping, CancellationToken cancellationToken)
    {
        var sessionId = environmentReader.GetEnvironmentVariable("XDG_SESSION_ID");
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            return [sessionId];
        }

        var result = await commandRunner.RunAsync("loginctl", "list-sessions --no-legend", cancellationToken);
        if (!result.Succeeded)
        {
            var detail = FirstNonEmpty(result.StandardError, result.StandardOutput)
                ?? $"loginctl list-sessions failed with exit code {result.ExitCode}.";
            throw new InvalidOperationException(detail);
        }

        var sessionIds = new List<string>();
        var lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (columns.Length < 3)
            {
                continue;
            }

            if (!string.Equals(columns[2], mapping.LocalUser, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(columns[0]) && !sessionIds.Contains(columns[0], StringComparer.Ordinal))
            {
                sessionIds.Add(columns[0]);
            }
        }

        return sessionIds;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }
        }

        return null;
    }
}
