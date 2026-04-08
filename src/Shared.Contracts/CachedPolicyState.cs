namespace Shared.Contracts;

public sealed record CachedPolicyState(
    string LocalUser,
    PolicyFetchResponse Response,
    DateTimeOffset CachedAtUtc);
