using System.Text.Json;
using Shared.Contracts;

namespace Agent.Core;

public sealed class FilePolicyCache(string directoryPath) : IPolicyCache
{
    public async Task<CachedPolicyState?> LoadAsync(string localUser, CancellationToken cancellationToken)
    {
        var path = GetPath(localUser);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(stream, SessionGuardJsonContext.Default.CachedPolicyState, cancellationToken);
    }

    public async Task SaveAsync(CachedPolicyState cachedPolicyState, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directoryPath);
        var path = GetPath(cachedPolicyState.LocalUser);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, cachedPolicyState, SessionGuardJsonContext.Default.CachedPolicyState, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private string GetPath(string localUser) => Path.Combine(directoryPath, $"{localUser}.policy.json");
}
