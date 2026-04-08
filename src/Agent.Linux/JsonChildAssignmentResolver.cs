using System.Text.Json;

namespace Agent.Linux;

public sealed class JsonChildAssignmentResolver : IChildAssignmentResolver
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _mappingFilePath;

    public JsonChildAssignmentResolver(string mappingFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingFilePath);
        _mappingFilePath = mappingFilePath;
    }

    public async Task<string?> ResolveChildIdAsync(string linuxUserName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(linuxUserName))
        {
            return null;
        }

        if (!File.Exists(_mappingFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_mappingFilePath);
        var assignments = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (assignments is null)
        {
            return null;
        }

        return assignments.TryGetValue(linuxUserName, out var childId) ? childId : null;
    }
}
