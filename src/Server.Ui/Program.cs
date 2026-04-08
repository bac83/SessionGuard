using Server.Ui.Components;
using Server.Ui.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<SessionGuardApiOptions>()
    .Bind(builder.Configuration.GetSection(SessionGuardApiOptions.SectionName));
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient<SessionGuardApiClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SessionGuardApiOptions>>().Value;
    client.BaseAddress = new Uri(options.ApiBaseUrl);
    if (!string.IsNullOrWhiteSpace(options.ApiKey))
    {
        client.DefaultRequestHeaders.Remove("X-SessionGuard-ApiKey");
        client.DefaultRequestHeaders.Add("X-SessionGuard-ApiKey", options.ApiKey);
    }
});
builder.Services.AddScoped<IAdminDashboardStore, ApiAdminDashboardStore>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
