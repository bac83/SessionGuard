using Agent.Core;
using Microsoft.Extensions.Logging;
using Shared.Contracts;

namespace Agent.Linux;

public sealed class LinuxSessionLockService(
    ICommandRunner commandRunner,
    IEnvironmentReader environmentReader,
    ILogger<LinuxSessionLockService> logger) : ISessionController
{
    public async Task LockAsync(UserChildMapping mapping, CancellationToken cancellationToken)
    {
        var sessions = await ResolveSessionsAsync(mapping, cancellationToken);
        if (sessions.Count == 0)
        {
            throw new InvalidOperationException($"Unable to resolve a login session to lock for local user '{mapping.LocalUser}'.");
        }

        logger.LogInformation(
            "Attempting to lock {SessionCount} login session(s) for local user {LocalUser}: {SessionIds}",
            sessions.Count,
            mapping.LocalUser,
            string.Join(", ", sessions.Select(session => session.Id)));

        foreach (var session in sessions)
        {
            var loginctlResult = await commandRunner.RunAsync("loginctl", $"lock-session {session.Id}", cancellationToken);
            if (loginctlResult.Succeeded)
            {
                logger.LogInformation(
                    "loginctl accepted lock request for session {SessionId} belonging to local user {LocalUser}",
                    session.Id,
                    mapping.LocalUser);
                continue;
            }

            var fallbackResult = await TryDesktopLockFallbackAsync(mapping, session, cancellationToken);
            if (!fallbackResult)
            {
                var detail = FirstNonEmpty(loginctlResult.StandardError, loginctlResult.StandardOutput)
                    ?? $"loginctl lock-session {session.Id} failed with exit code {loginctlResult.ExitCode}.";
                throw new InvalidOperationException(detail);
            }
        }
    }

    private async Task<List<LoginSession>> ResolveSessionsAsync(UserChildMapping mapping, CancellationToken cancellationToken)
    {
        var sessionId = environmentReader.GetEnvironmentVariable("XDG_SESSION_ID");
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            logger.LogDebug("Using XDG_SESSION_ID={SessionId} for local user {LocalUser}", sessionId, mapping.LocalUser);
            var xdgSession = await ResolveSessionDetailsAsync(sessionId, cancellationToken);
            var expectedUserId = await ResolveLocalUserIdAsync(mapping, cancellationToken);
            if (IsSessionOwnedByLocalUser(xdgSession, expectedUserId))
            {
                return [xdgSession];
            }

            logger.LogWarning(
                "Ignoring XDG_SESSION_ID={SessionId} for local user {LocalUser} because it resolved to uid {ResolvedUserId} instead of {ExpectedUserId}",
                sessionId,
                mapping.LocalUser,
                xdgSession.UserId ?? "<unknown>",
                expectedUserId ?? "<unknown>");
        }

        var result = await commandRunner.RunAsync("loginctl", "list-sessions --no-legend", cancellationToken);
        if (!result.Succeeded)
        {
            var detail = FirstNonEmpty(result.StandardError, result.StandardOutput)
                ?? $"loginctl list-sessions failed with exit code {result.ExitCode}.";
            throw new InvalidOperationException(detail);
        }

        logger.LogDebug("loginctl list-sessions output for {LocalUser}: {Output}", mapping.LocalUser, result.StandardOutput.Trim());
        var sessions = new List<LoginSession>();
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

            if (!string.IsNullOrWhiteSpace(columns[0]) && !sessions.Any(session => string.Equals(session.Id, columns[0], StringComparison.Ordinal)))
            {
                sessions.Add(new LoginSession(columns[0], columns[1], null));
            }
        }

        return sessions;
    }

    private async Task<LoginSession> ResolveSessionDetailsAsync(string sessionId, CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync(
            "loginctl",
            $"show-session {sessionId} --property=Id --property=User --property=Display",
            cancellationToken);

        if (!result.Succeeded)
        {
            logger.LogDebug(
                "loginctl show-session failed for session {SessionId}: {Detail}",
                sessionId,
                FirstNonEmpty(result.StandardError, result.StandardOutput)
                    ?? $"loginctl show-session {sessionId} failed with exit code {result.ExitCode}.");
            return new LoginSession(sessionId, null, null);
        }

        string? resolvedId = null;
        string? userId = null;
        string? display = null;

        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex];
            var value = line[(separatorIndex + 1)..];
            switch (key)
            {
                case "Id":
                    resolvedId = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "User":
                    userId = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "Display":
                    display = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
            }
        }

        return new LoginSession(resolvedId ?? sessionId, userId, display);
    }

    private async Task<bool> TryDesktopLockFallbackAsync(
        UserChildMapping mapping,
        LoginSession session,
        CancellationToken cancellationToken)
    {
        session = await EnrichSessionForDesktopFallbackAsync(session, cancellationToken);
        if (string.IsNullOrWhiteSpace(session.UserId))
        {
            logger.LogDebug(
                "Skipping desktop lock fallback for session {SessionId} because the numeric user id is unavailable",
                session.Id);
            return false;
        }

        var runtimeDirectory = $"/run/user/{session.UserId}";
        var busAddress = $"unix:path={runtimeDirectory}/bus";
        var display = string.IsNullOrWhiteSpace(session.Display) ? ":0" : session.Display;
        var environment = $"DISPLAY={display} XDG_RUNTIME_DIR={runtimeDirectory} DBUS_SESSION_BUS_ADDRESS={busAddress}";
        var attempts = new[]
        {
            new DesktopLockAttempt("cinnamon-screensaver-command", $"--lock", "Cinnamon screensaver"),
            new DesktopLockAttempt("gnome-screensaver-command", "-l", "GNOME screensaver")
        };

        foreach (var attempt in attempts)
        {
            var arguments = $"-u {mapping.LocalUser} -- env {environment} {attempt.Command} {attempt.Arguments}";
            CommandResult result;
            try
            {
                result = await commandRunner.RunAsync("runuser", arguments, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogDebug(
                    exception,
                    "{LockMethod} fallback could not be started for session {SessionId}",
                    attempt.Name,
                    session.Id);
                continue;
            }

            if (result.Succeeded)
            {
                logger.LogInformation(
                    "{LockMethod} fallback accepted lock request for session {SessionId} belonging to local user {LocalUser}",
                    attempt.Name,
                    session.Id,
                    mapping.LocalUser);
                return true;
            }

            logger.LogDebug(
                "{LockMethod} fallback failed for session {SessionId}: {Detail}",
                attempt.Name,
                session.Id,
                FirstNonEmpty(result.StandardError, result.StandardOutput)
                    ?? $"{attempt.Command} {attempt.Arguments} failed with exit code {result.ExitCode}.");
        }

        logger.LogWarning(
            "No desktop lock fallback accepted the lock request for session {SessionId} belonging to local user {LocalUser}",
            session.Id,
            mapping.LocalUser);

        return false;
    }

    private async Task<LoginSession> EnrichSessionForDesktopFallbackAsync(LoginSession session, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(session.UserId) && !string.IsNullOrWhiteSpace(session.Display))
        {
            return session;
        }

        var resolved = await ResolveSessionDetailsAsync(session.Id, cancellationToken);
        return session with
        {
            UserId = string.IsNullOrWhiteSpace(session.UserId) ? resolved.UserId : session.UserId,
            Display = string.IsNullOrWhiteSpace(session.Display) ? resolved.Display : session.Display
        };
    }

    private async Task<string?> ResolveLocalUserIdAsync(UserChildMapping mapping, CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync("id", $"-u {mapping.LocalUser}", cancellationToken);
        if (!result.Succeeded)
        {
            logger.LogDebug(
                "Unable to resolve uid for local user {LocalUser}: {Detail}",
                mapping.LocalUser,
                FirstNonEmpty(result.StandardError, result.StandardOutput)
                    ?? $"id -u {mapping.LocalUser} failed with exit code {result.ExitCode}.");
            return null;
        }

        var value = result.StandardOutput.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool IsSessionOwnedByLocalUser(LoginSession session, string? expectedUserId)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(expectedUserId))
        {
            return false;
        }

        return string.Equals(session.UserId, expectedUserId, StringComparison.Ordinal);
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

    private sealed record LoginSession(string Id, string? UserId, string? Display);

    private sealed record DesktopLockAttempt(string Command, string Arguments, string Name);
}
