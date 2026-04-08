using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;
using Server.Infrastructure.Persistence;
using Server.Infrastructure.Services;
using Shared.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddServerInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SessionGuardDbContext>();

var app = builder.Build();
await app.Services.InitializeServerInfrastructureAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.MapHealthChecks("/healthz");

var admin = app.MapGroup("/api/admin");
admin.MapGet("/dashboard", async Task<Ok<DashboardResponse>> (ISessionGuardRepository repository, CancellationToken cancellationToken) =>
{
    return TypedResults.Ok(await repository.GetDashboardAsync(cancellationToken));
});

admin.MapGet("/children", async Task<Ok<IReadOnlyList<ChildSummary>>> (ISessionGuardRepository repository, CancellationToken cancellationToken) =>
{
    return TypedResults.Ok(await repository.GetChildrenAsync(cancellationToken));
});

admin.MapPost("/children", async Task<Results<Ok<ChildSummary>, ValidationProblem>> (
    UpsertChildRequest request,
    ISessionGuardRepository repository,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.ChildId) || string.IsNullOrWhiteSpace(request.DisplayName) || request.DailyLimitMinutes < 0)
    {
        return TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["ChildId, DisplayName, and a non-negative DailyLimitMinutes are required."]
        });
    }

    return TypedResults.Ok(await repository.UpsertChildAsync(request, cancellationToken));
});

var agent = app.MapGroup("/api/agent");
agent.MapPost("/register", async Task<Results<Ok<AgentRegistrationResponse>, ValidationProblem>> (
    AgentRegistrationRequest request,
    ISessionGuardRepository repository,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.AgentId) || string.IsNullOrWhiteSpace(request.Hostname))
    {
        return TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["AgentId and Hostname are required."]
        });
    }

    return TypedResults.Ok(await repository.RegisterAgentAsync(request, cancellationToken));
});

agent.MapGet("/policies/{agentId}", async Task<Ok<PolicyFetchResponse>> (
    string agentId,
    string? childId,
    ISessionGuardRepository repository,
    CancellationToken cancellationToken) =>
{
    return TypedResults.Ok(await repository.GetPolicyAsync(agentId, childId, cancellationToken));
});

agent.MapPost("/usage", async Task<Results<Ok<UsageReportResponse>, NotFound, ValidationProblem>> (
    UsageReportRequest request,
    ISessionGuardRepository repository,
    SessionGuardDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.AgentId) || string.IsNullOrWhiteSpace(request.ChildId) || request.UsedMinutes < 0)
    {
        return TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["request"] = ["AgentId, ChildId, and a non-negative UsedMinutes are required."]
        });
    }

    if (!await dbContext.Agents.AnyAsync(x => x.AgentId == request.AgentId, cancellationToken))
    {
        return TypedResults.NotFound();
    }

    return TypedResults.Ok(await repository.SaveUsageReportAsync(request, cancellationToken));
});

app.Run();

public partial class Program;
