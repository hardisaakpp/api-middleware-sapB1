using System.Net;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Middleware.Infrastructure.WebPos;

internal sealed class WebPosClient : IWebPosClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebPosClient> _logger;

    public WebPosClient(HttpClient httpClient, ILogger<WebPosClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> SendAsync(HttpMethod method, string relativeUrl, string body, string token, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, relativeUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, MediaTypeNames.Application.Json)
        };
        request.Headers.Add("Authorization", token);

        _logger.LogDebug("WEBPOS → {Method} {Url}", method.Method, relativeUrl);

        var response = await _httpClient.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        // WEBPOS encodes both success AND business rejections as HTTP 200 or 400 alike — the
        // caller must parse `procesoCorrecto` in the body for either. Any other status (401,
        // 404, 5xx) is a genuine transport/availability failure, not a business answer.
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest)
        {
            return content;
        }

        _logger.LogWarning("WEBPOS ← {StatusCode} {Url} — {Error}", (int)response.StatusCode, relativeUrl, content);
        throw new WebPosException(response.StatusCode, content);
    }
}
