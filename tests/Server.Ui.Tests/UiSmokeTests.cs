using System.Net;
using System.Text;
using Server.Ui.Models;
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

    [Fact]
    public void ChildAgentSummaryBuilder_ReturnsAssignedAgentsAndOnlineStatus()
    {
        var snapshot = new DashboardSnapshot(
            [
                new ChildProfile("child-01", "Sara", 90, true, 15, 75, DateTimeOffset.UtcNow),
                new ChildProfile("child-02", "Mila", 120, true, 0, 120, DateTimeOffset.UtcNow)
            ],
            [
                new AgentStatus("agent-01", "1.2.3", "mond", "sara", "child-01", true, false, 15, 75, DateTimeOffset.UtcNow),
                new AgentStatus("agent-02", "1.2.3", "tablet", "sara", "child-01", false, false, 15, 75, DateTimeOffset.UtcNow)
            ],
            DateTimeOffset.UtcNow);

        var summary = ChildAgentSummaryBuilder.Build(snapshot, "child-01");

        Assert.Equal(2, summary.Agents.Count);
        Assert.Equal("Online", summary.StatusLabel);
        Assert.Equal("mond", summary.Agents[0].HostName);
        Assert.Equal("tablet", summary.Agents[1].HostName);
    }

    [Fact]
    public void ChildAgentSummaryBuilder_ReturnsAwaitingAgentWhenNoAssignmentExists()
    {
        var snapshot = new DashboardSnapshot(
            [new ChildProfile("child-02", "Mila", 120, true, 0, 120, DateTimeOffset.UtcNow)],
            [],
            DateTimeOffset.UtcNow);

        var summary = ChildAgentSummaryBuilder.Build(snapshot, "child-02");

        Assert.Empty(summary.Agents);
        Assert.Equal("Awaiting agent", summary.StatusLabel);
    }

    [Fact]
    public void ChildAgentSummaryBuilder_ReturnsAwaitingAgentWhenSnapshotIsNull()
    {
        var summary = ChildAgentSummaryBuilder.Build(null, "child-02");

        Assert.Empty(summary.Agents);
        Assert.Equal("Awaiting agent", summary.StatusLabel);
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
