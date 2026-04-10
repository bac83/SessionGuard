using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Shared.Contracts;

var statusFilePath = Environment.GetEnvironmentVariable("SESSIONGUARD_STATUS_FILE")
    ?? "/var/lib/sessionguard/status/agent-status.json";
var pollSeconds = ReadPollSeconds();
var runOnce = args.Any(arg => string.Equals(arg, "--once", StringComparison.OrdinalIgnoreCase));

if (runOnce)
{
    var snapshot = await ReadSnapshotAsync(statusFilePath);
    PrintStatus(snapshot);
    return snapshot is null ? 1 : 0;
}

return await RunTrayAsync(statusFilePath, TimeSpan.FromSeconds(pollSeconds));

static async Task<int> RunTrayAsync(string statusFilePath, TimeSpan interval)
{
    using var yad = StartYad();
    if (yad is null)
    {
        Console.Error.WriteLine("Unable to start 'yad'. Install the sessionguard-agent package dependencies or run Agent.Tray.dll --once for console status.");
        return 1;
    }

    while (!yad.HasExited)
    {
        var snapshot = await ReadSnapshotAsync(statusFilePath);
        var tooltip = BuildTooltip(snapshot, statusFilePath).Replace("\r", " ").Replace("\n", "\\n");
        var icon = snapshot is not null
            && (
                snapshot.IsLocked
                || (snapshot.RemainingMinutes == 0 && !snapshot.IsLocked)
                || !string.IsNullOrWhiteSpace(snapshot.Message))
            ? "dialog-warning"
            : "sessionguard";

        try
        {
            await yad.StandardInput.WriteLineAsync($"icon:{icon}");
            await yad.StandardInput.WriteLineAsync($"tooltip:{tooltip}");
            await yad.StandardInput.FlushAsync();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or IOException)
        {
            break;
        }

        await Task.Delay(interval);
    }

    return 0;
}

static Process? StartYad()
{
    try
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = "yad",
            ArgumentList =
            {
                "--notification",
                "--listen",
                "--image=sessionguard",
                "--text=SessionGuard",
                "--no-middle"
            },
            RedirectStandardInput = true,
            UseShellExecute = false
        });
    }
    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        return null;
    }
}

static async Task<AgentStatusSnapshot?> ReadSnapshotAsync(string statusFilePath)
{
    if (!File.Exists(statusFilePath))
    {
        return null;
    }

    try
    {
        await using var stream = File.OpenRead(statusFilePath);
        return await JsonSerializer.DeserializeAsync(stream, SessionGuardJsonContext.Default.AgentStatusSnapshot);
    }
    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or System.Security.SecurityException)
    {
        return null;
    }
}

static void PrintStatus(AgentStatusSnapshot? snapshot)
{
    if (snapshot is null)
    {
        Console.Error.WriteLine("No readable SessionGuard status snapshot was found.");
        return;
    }

    Console.WriteLine("SessionGuard Status");
    Console.WriteLine("-------------------");
    Console.WriteLine($"Agent:      {snapshot.AgentId}");
    Console.WriteLine($"Version:    {FirstNonEmpty(snapshot.AgentVersion, ResolveTrayVersion())}");
    Console.WriteLine($"User:       {snapshot.LocalUser}");
    Console.WriteLine($"Child:      {snapshot.ChildId}");
    Console.WriteLine($"Offline:    {(snapshot.IsOfflineMode ? "yes" : "no")}");
    Console.WriteLine($"Locked:     {(snapshot.IsLocked ? "yes" : "no")}");
    Console.WriteLine($"Used:       {snapshot.UsedMinutes} min");
    Console.WriteLine($"Remaining:  {(snapshot.RemainingMinutes is null ? "n/a" : $"{snapshot.RemainingMinutes} min")}");
    Console.WriteLine($"Updated:    {snapshot.RecordedAtUtc:O}");

    if (!string.IsNullOrWhiteSpace(snapshot.Message))
    {
        Console.WriteLine($"Message:    {snapshot.Message}");
    }
}

static string BuildTooltip(AgentStatusSnapshot? snapshot, string statusFilePath)
{
    if (snapshot is null)
    {
        return $"SessionGuard\\nNo readable status at {statusFilePath}";
    }

    var remaining = snapshot.RemainingMinutes is null ? "n/a" : $"{snapshot.RemainingMinutes} min";
    var message = string.IsNullOrWhiteSpace(snapshot.Message) ? string.Empty : $"\\n{snapshot.Message}";
    return $"SessionGuard {FirstNonEmpty(snapshot.AgentVersion, ResolveTrayVersion())}\\n{snapshot.LocalUser} -> {snapshot.ChildId}\\nUsed {snapshot.UsedMinutes} min, remaining {remaining}\\n{(snapshot.IsOfflineMode ? "Offline cache" : "Online")} / {(snapshot.IsLocked ? "Locked" : "Open")}{message}";
}

static int ReadPollSeconds()
{
    var raw = Environment.GetEnvironmentVariable("SESSIONGUARD_TRAY_POLL_SECONDS");
    return int.TryParse(raw, out var parsed) && parsed > 0 ? Math.Max(parsed, 10) : 60;
}

static string ResolveTrayVersion()
{
    var assembly = Assembly.GetExecutingAssembly();
    return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}

static string FirstNonEmpty(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return "unknown";
}
