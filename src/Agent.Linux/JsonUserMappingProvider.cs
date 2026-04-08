using Agent.Core;
using Shared.Contracts;

namespace Agent.Linux;

public sealed class JsonUserMappingProvider(string userMapPath) : IUserMappingProvider
{
    private readonly JsonChildAssignmentResolver _resolver = new(userMapPath);

    public async Task<IReadOnlyList<UserChildMapping>> GetMappingsAsync(CancellationToken cancellationToken)
    {
        var userName = Environment.UserName;
        var childId = await _resolver.ResolveChildIdAsync(userName, cancellationToken);
        return string.IsNullOrWhiteSpace(childId) ? [] : [new UserChildMapping(userName, childId)];
    }
}
