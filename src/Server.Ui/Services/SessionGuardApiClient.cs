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

    public async Task<ChildSummary?> SaveChildAsync(UpsertChildRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/admin/children", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ChildSummary>(cancellationToken);
    }
}
