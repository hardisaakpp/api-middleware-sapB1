using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.Sap;

internal sealed class SapSessionManager : ISapSessionManager
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SapSettings _settings;
    private readonly ILogger<SapSessionManager> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private string? _sessionToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public SapSessionManager(
        IHttpClientFactory httpClientFactory,
        IOptions<SapSettings> settings,
        ILogger<SapSessionManager> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> GetSessionTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_sessionToken is not null && DateTimeOffset.UtcNow < _expiresAt)
            return _sessionToken;

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_sessionToken is not null && DateTimeOffset.UtcNow < _expiresAt)
                return _sessionToken;

            await LoginAsync(cancellationToken);
            return _sessionToken!;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task InvalidateSessionAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _sessionToken = null;
            _expiresAt = DateTimeOffset.MinValue;
            _logger.LogInformation("SAP session invalidated");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Logging in to SAP Service Layer — CompanyDB={CompanyDb}", _settings.CompanyDb);

        var payload = JsonSerializer.Serialize(new
        {
            CompanyDB = _settings.CompanyDb,
            UserName = _settings.UserName,
            Password = _settings.Password
        });

        using var client = _httpClientFactory.CreateClient("SapLogin");
        using var request = new HttpRequestMessage(HttpMethod.Post, "Login")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new SapException(response.StatusCode, content);

        using var doc = JsonDocument.Parse(content);
        var sessionId = doc.RootElement.GetProperty("SessionId").GetString()
            ?? throw new InvalidOperationException("SAP login response is missing SessionId.");

        _sessionToken = sessionId;
        _expiresAt = DateTimeOffset.UtcNow.AddMinutes(25);

        _logger.LogInformation("SAP session established, expires at {ExpiresAt:O}", _expiresAt);
    }
}
