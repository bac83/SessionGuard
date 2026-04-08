using System.Text.Json;
using Agent.Core;
using Shared.Contracts;

namespace Agent.Linux;

public sealed class JsonUserMappingProvider(string userMapPath) : IUserMappingProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _userMapPath = userMapPath;

    public async Task<IReadOnlyList<UserChildMapping>> GetMappingsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_userMapPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_userMapPath);
        var mappings = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (mappings is null || mappings.Count == 0)
        {
            return [];
        }

        return mappings
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
            .Select(static entry => new UserChildMapping(entry.Key.Trim(), entry.Value.Trim()))
            .ToArray();
    }
}
