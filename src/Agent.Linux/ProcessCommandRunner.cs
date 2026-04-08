using System.Diagnostics;

namespace Agent.Linux;

public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Unable to start '{fileName}'.");
        var standardOutput = await process.StandardOutput.ReadToEndAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var standardError = await process.StandardError.ReadToEndAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new CommandResult(process.ExitCode, standardOutput, standardError);
    }
}
