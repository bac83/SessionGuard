using Agent.Core;
using Microsoft.Extensions.Logging;
using Shared.Contracts;

namespace Agent.Linux;

public sealed class LinuxIdleDetector(
    ICommandRunner commandRunner,
    IEnvironmentReader environmentReader,
    AgentLinuxOptions options,
    ILogger<LinuxIdleDetector> logger) : IIdleDetector
{
    public async Task<bool> IsActiveAsync(UserChildMapping mapping, CancellationToken cancellationToken)
    {
        var resolution = await ResolveSessionAsync(mapping, cancellationToken);
        if (resolution.Outcome == SessionResolutionOutcome.CommandFailed)
        {
            logger.LogDebug("loginctl unavailable for {LocalUser}; assuming active (fail-open)", mapping.LocalUser);
            return true;
        }

        if (resolution.Session is null)
        {
            logger.LogDebug("No login session for {LocalUser}; treating as inactive", mapping.LocalUser);
            return false;
        }

        var session = resolution.Session;
        var hints = resolution.Hints ?? await ReadSessionHintsAsync(session.Id, cancellationToken);

        if (hints.Locked == true)
        {
            logger.LogDebug("Session {SessionId} for {LocalUser} reports LockedHint=yes", session.Id, mapping.LocalUser);
            return false;
        }

        if (hints.Idle == true)
        {
            logger.LogDebug("Session {SessionId} for {LocalUser} reports IdleHint=yes", session.Id, mapping.LocalUser);
            return false;
        }

        var idleMilliseconds = await TryReadXprintIdleAsync(mapping, session, hints, cancellationToken);
        if (idleMilliseconds is { } ms && ms >= options.IdleThresholdSeconds * 1000L)
        {
            logger.LogDebug(
                "xprintidle reported {IdleMilliseconds} ms for {LocalUser} (threshold {ThresholdSeconds}s)",
                ms,
                mapping.LocalUser,
                options.IdleThresholdSeconds);
            return false;
        }

        return true;
    }

    private async Task<SessionResolution> ResolveSessionAsync(UserChildMapping mapping, CancellationToken cancellationToken)
    {
        var xdgSessionId = environmentReader.GetEnvironmentVariable("XDG_SESSION_ID");
        if (!string.IsNullOrWhiteSpace(xdgSessionId))
        {
            var expectedUserId = await ResolveLocalUserIdAsync(mapping, cancellationToken);
            var hints = await ReadSessionHintsAsync(xdgSessionId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(expectedUserId)
                && !string.IsNullOrWhiteSpace(hints.UserId)
                && string.Equals(hints.UserId, expectedUserId, StringComparison.Ordinal))
            {
                return new SessionResolution(
                    SessionResolutionOutcome.Found,
                    new ResolvedSession(xdgSessionId, hints.UserId, hints.Display),
                    hints);
            }

            logger.LogDebug(
                "Ignoring XDG_SESSION_ID={SessionId} for {LocalUser}: owner uid {ResolvedUid} != expected {ExpectedUid}",
                xdgSessionId,
                mapping.LocalUser,
                hints.UserId ?? "<unknown>",
                expectedUserId ?? "<unknown>");
        }

        var result = await commandRunner.RunAsync("loginctl", "list-sessions --no-legend", cancellationToken);
        if (!result.Succeeded)
        {
            logger.LogDebug("loginctl list-sessions failed (exit {ExitCode})", result.ExitCode);
            return new SessionResolution(SessionResolutionOutcome.CommandFailed, null, null);
        }

        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (columns.Length < 3)
            {
                continue;
            }

            if (string.Equals(columns[2], mapping.LocalUser, StringComparison.Ordinal))
            {
                return new SessionResolution(
                    SessionResolutionOutcome.Found,
                    new ResolvedSession(columns[0], columns[1], null),
                    null);
            }
        }

        return new SessionResolution(SessionResolutionOutcome.NoSessionForUser, null, null);
    }

    private async Task<string?> ResolveLocalUserIdAsync(UserChildMapping mapping, CancellationToken cancellationToken)
    {
        if (!LinuxIdentifiers.IsValidLocalUser(mapping.LocalUser))
        {
            return null;
        }

        var result = await commandRunner.RunAsync("id", $"-u -- '{mapping.LocalUser}'", cancellationToken);
        if (!result.Succeeded)
        {
            return null;
        }

        var value = result.StandardOutput.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private async Task<SessionHints> ReadSessionHintsAsync(string sessionId, CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync(
            "loginctl",
            $"show-session {sessionId} --property=LockedHint --property=IdleHint --property=Display --property=User",
            cancellationToken);

        if (!result.Succeeded)
        {
            return new SessionHints(null, null, null, null);
        }

        bool? locked = null;
        bool? idle = null;
        string? display = null;
        string? userId = null;

        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..];
            switch (key)
            {
                case "LockedHint":
                    locked = ParseHintBool(value);
                    break;
                case "IdleHint":
                    idle = ParseHintBool(value);
                    break;
                case "Display":
                    display = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "User":
                    userId = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
            }
        }

        return new SessionHints(locked, idle, display, userId);
    }

    private async Task<long?> TryReadXprintIdleAsync(
        UserChildMapping mapping,
        ResolvedSession session,
        SessionHints hints,
        CancellationToken cancellationToken)
    {
        var userId = !string.IsNullOrWhiteSpace(hints.UserId) ? hints.UserId : session.UserId;
        var display = !string.IsNullOrWhiteSpace(hints.Display) ? hints.Display : ":0";
        if (!LinuxIdentifiers.IsNumericId(userId)
            || !LinuxIdentifiers.IsXDisplay(display)
            || !LinuxIdentifiers.IsValidLocalUser(mapping.LocalUser))
        {
            return null;
        }

        var environment = $"DISPLAY={display} XDG_RUNTIME_DIR=/run/user/{userId} DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/{userId}/bus";
        var arguments = $"-u {mapping.LocalUser} -- env {environment} xprintidle";

        CommandResult result;
        try
        {
            result = await commandRunner.RunAsync("runuser", arguments, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "xprintidle could not be started for {LocalUser}", mapping.LocalUser);
            return null;
        }

        if (!result.Succeeded)
        {
            return null;
        }

        return long.TryParse(result.StandardOutput.Trim(), out var milliseconds) ? milliseconds : null;
    }

    private static bool? ParseHintBool(string value) => value.Trim().ToLowerInvariant() switch
    {
        "yes" or "true" or "1" => true,
        "no" or "false" or "0" => false,
        _ => null
    };

    private sealed record ResolvedSession(string Id, string? UserId, string? Display);

    private sealed record SessionHints(bool? Locked, bool? Idle, string? Display, string? UserId);

    private enum SessionResolutionOutcome
    {
        Found,
        NoSessionForUser,
        CommandFailed
    }

    private sealed record SessionResolution(SessionResolutionOutcome Outcome, ResolvedSession? Session, SessionHints? Hints);
}
