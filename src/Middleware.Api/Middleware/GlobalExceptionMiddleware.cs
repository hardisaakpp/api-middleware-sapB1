using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Middleware.Infrastructure.Sap;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Middleware.Api.Middleware;

public sealed class GlobalExceptionMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.Items[CorrelationIdHeader]?.ToString() ?? "unknown";

            if (ex is SapException { StatusCode: System.Net.HttpStatusCode.NotFound })
            {
                _logger.LogWarning("SAP 404 on {Method} {Path} — resource not found in SAP",
                    context.Request.Method,
                    context.Request.Path);
            }
            else
            {
                _logger.LogError(ex,
                    "Unhandled exception {ExceptionType} on {Method} {Path}",
                    ex.GetType().Name,
                    context.Request.Method,
                    context.Request.Path);
            }

            await WriteProblemDetailsAsync(context, ex, correlationId);
        }
    }

    private static async Task WriteProblemDetailsAsync(HttpContext context, Exception ex, string correlationId)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var (statusCode, title) = ex switch
        {
            SapThrottleException               => (HttpStatusCode.ServiceUnavailable, "SAP service busy"),
            BrokenCircuitException             => (HttpStatusCode.ServiceUnavailable, "SAP circuit breaker open"),
            TimeoutRejectedException           => (HttpStatusCode.GatewayTimeout, "SAP response timeout"),
            OperationCanceledException         => (HttpStatusCode.ServiceUnavailable, "Request cancelled"),
            ArgumentException                  => (HttpStatusCode.BadRequest, "Invalid request"),
            KeyNotFoundException               => (HttpStatusCode.NotFound, "Resource not found"),
            SapResponseException               => (HttpStatusCode.BadGateway, "SAP returned an unexpected response"),
            SapException { StatusCode: HttpStatusCode.NotFound } => (HttpStatusCode.NotFound, "Resource not found in SAP"),
            SapException                       => (HttpStatusCode.BadGateway, "SAP service error"),
            _                                  => (HttpStatusCode.InternalServerError, "An unexpected error occurred")
        };

        var problem = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = title,
            Detail = ex.Message,
            Extensions = { ["correlationId"] = correlationId }
        };

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
