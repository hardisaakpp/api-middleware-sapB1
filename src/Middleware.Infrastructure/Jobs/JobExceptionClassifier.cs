using System.Net;
using Middleware.Infrastructure.Sap;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Middleware.Infrastructure.Jobs;

internal static class JobExceptionClassifier
{
    public static bool IsTransient(Exception ex) => ex switch
    {
        SapThrottleException => true,
        BrokenCircuitException => true,
        TimeoutRejectedException => true,
        HttpRequestException => true,
        OperationCanceledException => true,
        _ => false
    };

    public static int MapToHttpStatus(Exception ex) => ex switch
    {
        SapException { StatusCode: HttpStatusCode.NotFound } => (int)HttpStatusCode.NotFound,
        SapException => (int)HttpStatusCode.BadGateway,
        SapResponseException => (int)HttpStatusCode.BadGateway,
        ArgumentException => (int)HttpStatusCode.BadRequest,
        SapThrottleException => (int)HttpStatusCode.ServiceUnavailable,
        BrokenCircuitException => (int)HttpStatusCode.ServiceUnavailable,
        TimeoutRejectedException => (int)HttpStatusCode.GatewayTimeout,
        _ => (int)HttpStatusCode.InternalServerError
    };
}
