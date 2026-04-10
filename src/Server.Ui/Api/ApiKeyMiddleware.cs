using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Shared.Contracts;

namespace Server.Ui.Api;

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
            || providedApiKey.Count != 1
            || !IsValidApiKey(providedApiKey[0], configuredApiKey))
        {
            logger.LogWarning("Rejected API request for {Path} because the API key header was missing or invalid.", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                new ApiErrorResponse("api_key_invalid", "Missing or invalid API key."),
                cancellationToken: context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static bool IsValidApiKey(string? providedApiKey, string configuredApiKey)
    {
        if (string.IsNullOrEmpty(providedApiKey))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedApiKey);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredApiKey);
        if (providedBytes.Length != configuredBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }
}

public sealed class ApiSecurityOptions
{
    public const string SectionName = "SessionGuard:Security";
    public const string HeaderName = "X-SessionGuard-ApiKey";

    public string? ApiKey { get; set; }
}
