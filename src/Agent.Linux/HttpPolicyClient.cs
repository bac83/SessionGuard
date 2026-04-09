using System.Net.Http.Json;
using Agent.Core;
using Shared.Contracts;

namespace Agent.Linux;

public sealed class HttpPolicyClient : IPolicyClient
{
    private readonly HttpClient _httpClient;
    private readonly AgentOptions _agentOptions;

    public HttpPolicyClient(HttpClient httpClient, AgentOptions agentOptions, AgentLinuxOptions linuxOptions)
    {
        _httpClient = httpClient;
        _agentOptions = agentOptions;

        if (!string.IsNullOrWhiteSpace(linuxOptions.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Remove("X-SessionGuard-ApiKey");
            _httpClient.DefaultRequestHeaders.Add("X-SessionGuard-ApiKey", linuxOptions.ApiKey);
        }
    }

    public async Task RegisterAsync(UserChildMapping mapping, CancellationToken cancellationToken)
    {
        var payload = new AgentRegistrationRequest(
            _agentOptions.AgentId,
            Environment.MachineName,
            mapping.LocalUser,
            mapping.ChildId,
            _agentOptions.AgentVersion);

        using var response = await _httpClient.PostAsJsonAsync("/api/agent/register", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<PolicyFetchResponse> FetchPolicyAsync(UserChildMapping mapping, CancellationToken cancellationToken)
    {
        return await _httpClient.GetFromJsonAsync<PolicyFetchResponse>(
                   $"/api/agent/policies/{Uri.EscapeDataString(_agentOptions.AgentId)}?childId={Uri.EscapeDataString(mapping.ChildId)}",
                   cancellationToken)
               ?? new PolicyFetchResponse(_agentOptions.AgentId, mapping.ChildId, null, DateTimeOffset.UtcNow);
    }

    public async Task<UsageReportResponse> ReportUsageAsync(UsageReportRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/agent/usage", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UsageReportResponse>(cancellationToken)
               ?? new UsageReportResponse(request.AgentId, request.ChildId, request.UsedMinutes, null, DateTimeOffset.UtcNow);
    }
}
