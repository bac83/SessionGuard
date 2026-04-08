namespace Shared.Contracts;

public sealed record AgentRegistrationRequest(
    string AgentId,
    string Hostname,
    string? LocalUser,
    string? ChildId,
    string AgentVersion);
