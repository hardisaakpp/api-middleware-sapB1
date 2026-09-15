using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.WebPos;

internal sealed class WebPosSessionManager : IWebPosSessionManager
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebPosSettings _settings;
    private readonly ILogger<WebPosSessionManager> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public WebPosSessionManager(IHttpClientFactory httpClientFactory, IOptions<WebPosSettings> settings, ILogger<WebPosSessionManager> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _token;
        }

        await _semaphore.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _token;
            }

            await LoginAsync(ct);
            return _token!;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task InvalidateAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            _token = null;
            _expiresAt = DateTimeOffset.MinValue;
            _logger.LogInformation("WEBPOS session invalidated");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task LoginAsync(CancellationToken ct)
    {
        _logger.LogInformation("Logging in to WEBPOS");

        var payload = JsonSerializer.Serialize(new { username = _settings.UserName, password = _settings.Password });

        using var client = _httpClientFactory.CreateClient("WebPosLogin");
        using var request = new HttpRequestMessage(HttpMethod.Post, "apilogin/external")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw LoginFailure(client.BaseAddress, response.StatusCode, content);
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        if (!root.TryGetProperty("retorno", out var retorno) ||
            retorno.ValueKind != JsonValueKind.Object ||
            !retorno.TryGetProperty("token", out var tokenEl) ||
            tokenEl.ValueKind != JsonValueKind.String)
        {
            throw LoginFailure(client.BaseAddress, response.StatusCode, content);
        }

        _token = tokenEl.GetString();

        // Confirmed live against the real WEBPOS test server: expiration_token is in
        // MILLISECONDS (e.g. 720000000 ms ≈ 8.3 days) — as seconds that would be ~22.8 years,
        // which would never trigger a proactive refresh.
        var expirationSeconds = retorno.TryGetProperty("expiration_token", out var expEl) &&
            expEl.TryGetInt64(out var expMs) && expMs > 0
                ? expMs / 1000d
                : 25 * 60;

        // Refresh a bit before the real expiry so a request never races an about-to-expire token.
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expirationSeconds - 60));

        _logger.LogInformation("WEBPOS session established, expires at {ExpiresAt:O}", _expiresAt);
    }

    /// <summary>
    /// A failed login is almost always configuration, not WEBPOS being down: wrong credentials, or a
    /// BaseUrl that is not the server root (WEBPOS then answers "token inválido" even to the login
    /// itself). The message names the URL that was called so support can spot it in the bitácora.
    /// </summary>
    private WebPosException LoginFailure(Uri? baseAddress, System.Net.HttpStatusCode status, string content)
    {
        var url = $"{baseAddress}apilogin/external";
        _logger.LogError("WEBPOS login failed at {Url} (HTTP {Status}): {Content}", url, (int)status, content);
        return new WebPosException(
            status,
            $"Login WEBPOS falló en {url} (HTTP {(int)status}): {content}. Verifique WebPos:BaseUrl (debe ser la raíz del servidor, sin '/external') y las credenciales.");
    }
}
