using System.Diagnostics;
using Middleware.Application.Abstractions;

namespace Middleware.Api.Middleware;

public sealed class ApiLogMiddleware
{
    private static readonly HashSet<string> SkipPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/health/ready"
    };

    private static readonly string[] SkipPrefixes = ["/swagger"];

    // Truncate bodies to avoid oversized rows
    private const int MaxBodyLength = 2000;

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiLogMiddleware> _logger;

    public ApiLogMiddleware(RequestDelegate next, ILogger<ApiLogMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    // IApiLogRepository is scoped — injected per-request via method injection
    public async Task InvokeAsync(HttpContext context, IApiLogRepository repo)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (SkipPaths.Contains(path) || SkipPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))) // NOSONAR
        {
            await _next(context);
            return;
        }

        // Allow the request body to be read more than once
        context.Request.EnableBuffering();

        var requestSummary = await ReadRequestSummaryAsync(context.Request);

        // Replace the response stream so we can capture the body
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        var sw = Stopwatch.StartNew();
        Exception? caughtException = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            caughtException = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            context.Response.Body = originalBody;

            // Copy buffered response back to the real stream
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody);

            var responseSummary = await ReadResponseSummaryAsync(buffer, context.Response.StatusCode, context.Request.Method, caughtException);
            var usuario = context.User?.Identity?.Name ?? "system";
            var endpoint = $"{context.Request.Method} {context.Request.Path.Value}";

            var entry = new ApiLogEntry(
                Endpoint:   endpoint,
                Request:    requestSummary,
                Response:   responseSummary,
                Usuario:    usuario,
                StatusCode: context.Response.StatusCode,
                DurationMs: (int)sw.ElapsedMilliseconds);

            try
            {
                await repo.InsertAsync(entry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ApiLog insert failed for {Endpoint}", endpoint);
            }
        }
    }

    private static async Task<string?> ReadRequestSummaryAsync(HttpRequest request)
    {
        if (request.QueryString.HasValue && request.ContentLength is null or 0)
            return Truncate(request.QueryString.Value);

        if (request.ContentLength > 0)
        {
            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0;
            return Truncate(body);
        }

        return request.QueryString.HasValue ? Truncate(request.QueryString.Value) : null;
    }

    private static async Task<string?> ReadResponseSummaryAsync(MemoryStream buffer, int statusCode, string method, Exception? ex)
    {
        if (ex is not null)
            return Truncate(ex.Message);

        if (statusCode < 400 && method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        return Truncate(body);
    }

    private static string? Truncate(string? value)
    {
        if (value is null)
            return null;

        return value.Length <= MaxBodyLength ? value : value[..MaxBodyLength];
    }
}
