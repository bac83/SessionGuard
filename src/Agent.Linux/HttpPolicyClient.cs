using System.Net.Http.Json;
using Agent.Core;
using Shared.Contracts;

namespace Agent.Linux;

public sealed class HttpPolicyClient(HttpClient httpClient, AgentOptions agentOptions) : IPolicyClient
{
    public async Task RegisterAsync(UserChildMapping mapping, CancellationToken cancellationToken)
    {
        var payload = new AgentRegistrationRequest(
            agentOptions.AgentId,
            Environment.MachineName,
            mapping.LocalUser,
            mapping.ChildId,
            agentOptions.AgentVersion);

        using var response = await httpClient.PostAsJsonAsync("/api/agent/register", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<PolicyFetchResponse> FetchPolicyAsync(UserChildMapping mapping, CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync<PolicyFetchResponse>(
                   $"/api/agent/policy/{Uri.EscapeDataString(agentOptions.AgentId)}?childId={Uri.EscapeDataString(mapping.ChildId)}",
                   cancellationToken)
               ?? new PolicyFetchResponse(agentOptions.AgentId, mapping.ChildId, null, DateTimeOffset.UtcNow);
    }

    public async Task ReportUsageAsync(UsageReportRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/agent/usage", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
