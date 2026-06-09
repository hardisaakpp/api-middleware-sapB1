namespace Middleware.Infrastructure.Sap;

// TODO: Low-level HTTP wrapper for SAP Business One Service Layer REST API.
//       Registered as a typed HttpClient; Polly resilience pipeline applied at IHttpClientBuilder registration.
public interface ISapServiceLayerClient
{
    public Task<string> SendAsync(
        HttpMethod method,
        string relativeUrl,
        string? body,
        string sessionToken,
        CancellationToken cancellationToken = default);
}
