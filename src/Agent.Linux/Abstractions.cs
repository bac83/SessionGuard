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

public interface IChildAssignmentResolver
{
    Task<string?> ResolveChildIdAsync(string linuxUserName, CancellationToken cancellationToken = default);
}

public sealed class SystemEnvironmentReader : IEnvironmentReader
{
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
}
