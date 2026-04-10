using Server.Ui.Components;
using Server.Infrastructure;
using Server.Infrastructure.Persistence;
using Server.Ui.Api;
using Server.Ui.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ApiSecurityOptions>()
    .Bind(builder.Configuration.GetSection(ApiSecurityOptions.SectionName));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddServerInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SessionGuardDbContext>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<IAdminDashboardStore, ApiAdminDashboardStore>();

var app = builder.Build();
await app.Services.InitializeServerInfrastructureAsync();
var hasHttpsEndpoint = HasConfiguredHttpsEndpoint(app.Configuration);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase),
    apiApp =>
    {
        apiApp.UseExceptionHandler();
    });

app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase),
    uiApp =>
    {
        if (!app.Environment.IsDevelopment() && hasHttpsEndpoint)
        {
            uiApp.UseExceptionHandler("/Error", createScopeForErrors: true);
        }
        else
        {
            uiApp.UseExceptionHandler();
        }
    });

if (!app.Environment.IsDevelopment() && hasHttpsEndpoint)
{
    app.UseHsts();
}

app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase),
    uiApp =>
    {
        uiApp.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    });
if (hasHttpsEndpoint)
{
    app.UseHttpsRedirection();
}
app.UseMiddleware<ApiKeyMiddleware>();
app.UseAntiforgery();

app.MapHealthChecks("/healthz");
app.MapSessionGuardApi();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

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
