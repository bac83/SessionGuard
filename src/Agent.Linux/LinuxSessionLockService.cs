using Agent.Core;
using Shared.Contracts;

namespace Agent.Linux;

public sealed class LinuxSessionLockService(ICommandRunner commandRunner, IEnvironmentReader environmentReader) : ISessionController
{
    public async Task LockAsync(UserChildMapping mapping, CancellationToken cancellationToken)
    {
        var sessionId = environmentReader.GetEnvironmentVariable("XDG_SESSION_ID");
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var result = await commandRunner.RunAsync("loginctl", $"lock-session {sessionId}", cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.StandardError.Trim());
        }
    }
}
