using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Server.Api;
using Server.Infrastructure;
using Server.Infrastructure.Persistence;
using Server.Infrastructure.Services;
using Shared.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ApiSecurityOptions>()
    .Bind(builder.Configuration.GetSection(ApiSecurityOptions.SectionName));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddServerInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SessionGuardDbContext>();

var app = builder.Build();
await app.Services.InitializeServerInfrastructureAsync();
var hasHttpsEndpoint = HasConfiguredHttpsEndpoint(app.Configuration);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
if (hasHttpsEndpoint)
{
    app.UseHttpsRedirection();
}
app.UseMiddleware<ApiKeyMiddleware>();
app.MapHealthChecks("/healthz");

static BadRequest<ApiErrorResponse> ValidationError(string message) =>
    TypedResults.BadRequest(new ApiErrorResponse("validation_failed", message));

static NotFound<ApiErrorResponse> NotFoundError(string message) =>
    TypedResults.NotFound(new ApiErrorResponse("not_found", message));

var admin = app.MapGroup("/api/admin");
admin.MapGet("/dashboard", async Task<Ok<DashboardResponse>> (ISessionGuardRepository repository, CancellationToken cancellationToken) =>
{
    return TypedResults.Ok(await repository.GetDashboardAsync(cancellationToken));
});

admin.MapGet("/children", async Task<Ok<IReadOnlyList<ChildSummary>>> (ISessionGuardRepository repository, CancellationToken cancellationToken) =>
{
    return TypedResults.Ok(await repository.GetChildrenAsync(cancellationToken));
});

admin.MapPost("/children", async Task<Results<Ok<ChildSummary>, BadRequest<ApiErrorResponse>>> (
    UpsertChildRequest request,
    ISessionGuardRepository repository,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.ChildId) || string.IsNullOrWhiteSpace(request.DisplayName) || request.DailyLimitMinutes < 0)
    {
        return ValidationError("ChildId, DisplayName, and a non-negative DailyLimitMinutes are required.");
    }

    return TypedResults.Ok(await repository.UpsertChildAsync(request, cancellationToken));
});

var agent = app.MapGroup("/api/agent");
agent.MapPost("/register", async Task<Results<Ok<AgentRegistrationResponse>, BadRequest<ApiErrorResponse>>> (
    AgentRegistrationRequest request,
    ISessionGuardRepository repository,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.AgentId) || string.IsNullOrWhiteSpace(request.Hostname))
    {
        return ValidationError("AgentId and Hostname are required.");
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

agent.MapPost("/usage", async Task<Results<Ok<UsageReportResponse>, NotFound<ApiErrorResponse>, BadRequest<ApiErrorResponse>>> (
    UsageReportRequest request,
    ISessionGuardRepository repository,
    SessionGuardDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.AgentId) || string.IsNullOrWhiteSpace(request.ChildId) || request.UsedMinutes < 0)
    {
        return ValidationError("AgentId, ChildId, and a non-negative UsedMinutes are required.");
    }

    var normalizedAgentId = request.AgentId.Trim();
    if (!await dbContext.Agents.AnyAsync(x => x.AgentId == normalizedAgentId, cancellationToken))
    {
        return NotFoundError("AgentId is unknown.");
    }

    return TypedResults.Ok(await repository.SaveUsageReportAsync(request, cancellationToken));
});

app.Run();

static bool HasConfiguredHttpsEndpoint(IConfiguration configuration)
{
    var urls = configuration["ASPNETCORE_URLS"];
    if (!string.IsNullOrWhiteSpace(urls))
    {
        return urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    return configuration.GetSection("Kestrel:Endpoints")
        .GetChildren()
        .Select(endpoint => endpoint["Url"])
        .Any(url => !string.IsNullOrWhiteSpace(url) && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}

public partial class Program;
