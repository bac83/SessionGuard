namespace Agent.Linux;

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default);
}

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IEnvironmentReader
{
    string? GetEnvironmentVariable(string name);
}

public sealed record SessionState(bool IsActive, bool IsLocked, string? UserName)
{
    public static SessionState Inactive(string? userName = null) => new(false, true, userName);
}

public interface ISessionStateProvider
{
    Task<SessionState> GetStateAsync(CancellationToken cancellationToken = default);
}

public interface IChildAssignmentResolver
{
    Task<string?> ResolveChildIdAsync(string linuxUserName, CancellationToken cancellationToken = default);
}

public sealed class SystemEnvironmentReader : IEnvironmentReader
{
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
}
