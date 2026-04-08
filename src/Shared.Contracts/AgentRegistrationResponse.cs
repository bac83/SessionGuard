namespace Shared.Contracts;

public sealed record AgentRegistrationResponse(
    string AgentId,
    DateTimeOffset RegisteredAtUtc);
