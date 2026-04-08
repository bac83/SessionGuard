using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shared.Contracts;

namespace Server.Api.Tests;

public sealed class ApiEndpointsTests
{
    [Fact]
    public async Task AdminAndAgentEndpoints_WorkAgainstSQLitePersistence()
    {
        var sqlitePath = CreateSqlitePath();
        using var factory = new SessionGuardWebApplicationFactory(sqlitePath);
        using var client = factory.CreateClient();

        var childResponse = await client.PostAsJsonAsync("/api/admin/children", new UpsertChildRequest("child-01", "Sara", 90, true));
        Assert.Equal(HttpStatusCode.OK, childResponse.StatusCode);

        var registerResponse = await client.PostAsJsonAsync("/api/agent/register", new AgentRegistrationRequest("agent-01", "kid-laptop", "sara", "child-01", "1.0.0"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var policyResponse = await client.GetAsync("/api/agent/policies/agent-01?childId=child-01");
        Assert.Equal(HttpStatusCode.OK, policyResponse.StatusCode);
        var policy = await policyResponse.Content.ReadFromJsonAsync<PolicyFetchResponse>();
        Assert.NotNull(policy);
        Assert.NotNull(policy!.Policy);
        Assert.Equal(90, policy.Policy!.DailyLimitMinutes);

        var usageResponse = await client.PostAsJsonAsync(
            "/api/agent/usage",
            new UsageReportRequest("agent-01", "child-01", "sara", new DateOnly(2026, 4, 8), 35, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.OK, usageResponse.StatusCode);
        var usage = await usageResponse.Content.ReadFromJsonAsync<UsageReportResponse>();
        Assert.NotNull(usage);
        Assert.Equal(55, usage!.RemainingMinutes);

        var dashboardResponse = await client.GetAsync("/api/admin/dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        var dashboard = await dashboardResponse.Content.ReadFromJsonAsync<DashboardResponse>();
        Assert.NotNull(dashboard);
        Assert.Single(dashboard!.Children);
        Assert.Single(dashboard.Agents);
    }

    private static string CreateSqlitePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "sessionguard-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "sessionguard.db");
    }

    private sealed class SessionGuardWebApplicationFactory(string sqlitePath) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("SessionGuard:Storage:SqlitePath", sqlitePath);
        }
    }
}
