using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Middleware.Api.Configuration;

namespace Middleware.Api.Middleware;

/// <summary>
/// Requires a shared-secret header on every request. Liveness/readiness probes at /health are
/// exempt so container orchestrators can keep monitoring the app without credentials — see
/// docs/architecture.md's description of /health (liveness) and /health/ready (readiness).
/// </summary>
public sealed class ApiKeyMiddleware
{
    private static readonly string[] ExemptPathPrefixes = ["/health"];

    private readonly RequestDelegate _next;
    private readonly IOptions<ApiKeySettings> _settings;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(
        RequestDelegate next,
        IOptions<ApiKeySettings> settings,
        IHostEnvironment environment,
        ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _settings = settings;
        _environment = environment;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (Array.Exists(ExemptPathPrefixes, prefix => context.Request.Path.StartsWithSegments(prefix)))
        {
            await _next(context);
            return;
        }

        var configuredKey = _settings.Value.Value;

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            if (_environment.IsDevelopment())
            {
                await _next(context);
                return;
            }

            _logger.LogError("ApiKey:Value is not configured for this environment — rejecting all requests until it is set.");
            await WriteUnauthorizedAsync(context, "API key is not configured on the server.");
            return;
        }

        var provided = context.Request.Headers[_settings.Value.HeaderName].ToString();
        if (!FixedTimeEquals(provided, configuredKey))
        {
            _logger.LogWarning("Rejected request to {Path}: missing or invalid {HeaderName}.", // NOSONAR
                context.Request.Path, _settings.Value.HeaderName);
            await WriteUnauthorizedAsync(context, "Missing or invalid API key.");
            return;
        }

        await _next(context);
    }

    // Ordinal length/byte comparison would short-circuit on the first mismatched byte,
    // leaking key length/prefix via response timing; compare fixed-length hashes instead.
    private static bool FixedTimeEquals(string provided, string expected)
    {
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Detail = detail
        };

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
