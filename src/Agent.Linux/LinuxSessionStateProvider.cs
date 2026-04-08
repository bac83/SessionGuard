namespace Agent.Linux;

public sealed class LinuxSessionStateProvider : ISessionStateProvider
{
    private readonly ICommandRunner _commandRunner;
    private readonly IEnvironmentReader _environmentReader;

    public LinuxSessionStateProvider(ICommandRunner commandRunner, IEnvironmentReader environmentReader)
    {
        _commandRunner = commandRunner;
        _environmentReader = environmentReader;
    }

    public async Task<SessionState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var sessionId = _environmentReader.GetEnvironmentVariable("XDG_SESSION_ID");
        var userName = _environmentReader.GetEnvironmentVariable("USER");

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return SessionState.Inactive(userName);
        }

        var result = await _commandRunner
            .RunAsync("loginctl", $"show-session {sessionId} -p Active -p LockedHint", cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return SessionState.Inactive(userName);
        }

        var values = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        var isActive = values.TryGetValue("Active", out var activeValue) && activeValue.Equals("yes", StringComparison.OrdinalIgnoreCase);
        var isLocked = values.TryGetValue("LockedHint", out var lockedValue) && lockedValue.Equals("yes", StringComparison.OrdinalIgnoreCase);

        return new SessionState(isActive, isLocked, userName);
    }
}
