using Microsoft.Extensions.Options;

namespace Server.Api;

public sealed class ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger, IOptions<ApiSecurityOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var configuredApiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiSecurityOptions.HeaderName, out var providedApiKey)
            || !string.Equals(providedApiKey.ToString(), configuredApiKey, StringComparison.Ordinal))
        {
            logger.LogWarning("Rejected API request for {Path} because the API key header was missing or invalid.", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid API key." });
            return;
        }

        await next(context);
    }
}

public sealed class ApiSecurityOptions
{
    public const string SectionName = "SessionGuard:Security";
    public const string HeaderName = "X-SessionGuard-ApiKey";

    public string? ApiKey { get; set; }
}
