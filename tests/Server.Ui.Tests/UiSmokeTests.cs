using System.Net;
using System.Text;
using Shared.Contracts;
using Server.Ui.Services;

namespace Server.Ui.Tests;

public sealed class UiSmokeTests
{
    [Fact]
    public async Task GetDashboardAsync_DeserializesDashboardPayload()
    {
        const string payload = """
        {
          "children": [
            {
              "childId": "child-01",
              "displayName": "Sara",
              "dailyLimitMinutes": 90,
              "isEnabled": true,
              "updatedAtUtc": "2026-04-08T12:00:00+00:00"
            }
          ],
          "agents": []
        }
        """;

        using var client = new HttpClient(new StubMessageHandler(payload))
        {
            BaseAddress = new Uri("http://localhost:8080")
        };

        var apiClient = new SessionGuardApiClient(client);
        var dashboard = await apiClient.GetDashboardAsync();

        Assert.Single(dashboard.Children);
        Assert.Equal("child-01", dashboard.Children[0].ChildId);
    }

    [Fact]
    public async Task SaveChildAsync_ThrowsApiErrorMessageOnRejectedRequest()
    {
        const string payload = """
        {
          "error": "validation_failed",
          "message": "ChildId is required."
        }
        """;

        using var client = new HttpClient(new StubMessageHandler(payload, HttpStatusCode.BadRequest))
        {
            BaseAddress = new Uri("http://localhost:8080")
        };

        var apiClient = new SessionGuardApiClient(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            apiClient.SaveChildAsync(new UpsertChildRequest("", "Sara", 90, true)));

        Assert.Equal("ChildId is required.", exception.Message);
    }

    private sealed class StubMessageHandler(string payload, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
