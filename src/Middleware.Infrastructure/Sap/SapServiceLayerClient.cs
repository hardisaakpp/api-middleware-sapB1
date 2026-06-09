using System.Net;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Middleware.Infrastructure.Sap;

internal sealed class SapServiceLayerClient : ISapServiceLayerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SapServiceLayerClient> _logger;

    public SapServiceLayerClient(HttpClient httpClient, ILogger<SapServiceLayerClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> SendAsync(
        HttpMethod method,
        string relativeUrl,
        string? body,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);

        if (!string.IsNullOrEmpty(sessionToken))
        {
            request.Headers.Add("Cookie", $"B1SESSION={sessionToken}");
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, MediaTypeNames.Application.Json);
        }

        _logger.LogDebug("SAP → {Method} {Url}", method.Method, relativeUrl);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("SAP ← {StatusCode} {Url} — {Error}", (int)response.StatusCode, relativeUrl, content);
            throw new SapException(response.StatusCode, content);
        }

        return content;
    }
}
