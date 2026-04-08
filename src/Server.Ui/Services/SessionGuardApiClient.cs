using System.Net.Http.Json;
using Shared.Contracts;

namespace Server.Ui.Services;

public sealed class SessionGuardApiClient(HttpClient httpClient)
{
    public async Task<DashboardResponse> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<DashboardResponse>("/api/admin/dashboard", cancellationToken)
               ?? new DashboardResponse([], []);
    }

    public async Task<ChildSummary> SaveChildAsync(UpsertChildRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/admin/children", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken);
            var message = error?.Message ?? $"The server rejected the child request ({(int)response.StatusCode}).";
            throw new InvalidOperationException(message);
        }

        return await response.Content.ReadFromJsonAsync<ChildSummary>(cancellationToken)
            ?? throw new InvalidOperationException("The server returned an empty child response.");
    }
}
