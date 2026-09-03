using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Middleware.Infrastructure.Resilience;

internal static class ResiliencePolicies
{
    internal static IHttpResiliencePipelineBuilder AddSapResiliencePipeline(this IHttpClientBuilder builder)
    {
        return builder.AddResilienceHandler("sap-pipeline", pipeline =>
        {
            // Outermost: each retry attempt gets a fresh timeout budget
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is HttpRequestException ||
                    args.Outcome.Result?.StatusCode is >= HttpStatusCode.InternalServerError ||
                    args.Outcome.Result?.StatusCode == HttpStatusCode.RequestTimeout)
            });

            // Inside retry: counts each individual attempt; open circuit fails fast before the timeout fires
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromSeconds(30),
                FailureRatio = 0.5,
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30)
            });

            // Innermost: 30 s per attempt — resets on every retry
            pipeline.AddTimeout(new HttpTimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(30)
            });
        });
    }

    internal static IHttpResiliencePipelineBuilder AddWebPosResiliencePipeline(this IHttpClientBuilder builder)
    {
        return builder.AddResilienceHandler("webpos-pipeline", pipeline =>
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is HttpRequestException ||
                    args.Outcome.Result?.StatusCode is >= HttpStatusCode.InternalServerError ||
                    args.Outcome.Result?.StatusCode == HttpStatusCode.RequestTimeout)
            });

            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromSeconds(30),
                FailureRatio = 0.5,
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30)
            });

            pipeline.AddTimeout(new HttpTimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(30)
            });
        });
    }
}
