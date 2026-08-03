using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public SapSessionManager(
        IHttpClientFactory httpClientFactory,
        IOptions<SapSettings> settings,
        ILogger<SapSessionManager> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<string> GetSessionTokenAsync(CancellationToken cancellationToken = default)
        => GetSessionTokenAsync(_settings.CompanyDb, _settings.Password, cancellationToken);

    public async Task<string> GetSessionTokenAsync(string companyDb, string password, CancellationToken cancellationToken = default)
    {
        var session = _sessions.GetOrAdd(companyDb, _ => new Session());

        if (session.Token is not null && DateTimeOffset.UtcNow < session.ExpiresAt)
        {
            return session.Token;
        }

        await session.Semaphore.WaitAsync(cancellationToken);
        try
        {
            if (session.Token is not null && DateTimeOffset.UtcNow < session.ExpiresAt)
            {
                return session.Token;
            }

            await LoginAsync(companyDb, password, session, cancellationToken);
            return session.Token!;
        }
        finally
        {
            session.Semaphore.Release();
        }
    }

    public Task InvalidateSessionAsync(CancellationToken cancellationToken = default)
        => InvalidateSessionAsync(_settings.CompanyDb, cancellationToken);

    public async Task InvalidateSessionAsync(string companyDb, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(companyDb, out var session))
        {
            return;
        }

        await session.Semaphore.WaitAsync(cancellationToken);
        try
        {
            session.Token = null;
            session.ExpiresAt = DateTimeOffset.MinValue;
            _logger.LogInformation("SAP session invalidated — CompanyDB={CompanyDb}", companyDb);
        }
        finally
        {
            session.Semaphore.Release();
        }
    }

    private async Task LoginAsync(string companyDb, string password, Session session, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Logging in to SAP Service Layer — CompanyDB={CompanyDb}", companyDb);

        var payload = JsonSerializer.Serialize(new
        {
            CompanyDB = companyDb,
            UserName = _settings.UserName,
            Password = password
        });

        using var client = _httpClientFactory.CreateClient("SapLogin");
        using var request = new HttpRequestMessage(HttpMethod.Post, "Login")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SapException(response.StatusCode, content);
        }

        using var doc = JsonDocument.Parse(content);
        var sessionId = doc.RootElement.GetProperty("SessionId").GetString()
            ?? throw new InvalidOperationException("SAP login response is missing SessionId.");

        session.Token = sessionId;
        session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(25);

        _logger.LogInformation("SAP session established — CompanyDB={CompanyDb}, expires at {ExpiresAt:O}", companyDb, session.ExpiresAt);
    }

    private sealed class Session
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public string? Token;
        public DateTimeOffset ExpiresAt = DateTimeOffset.MinValue;
    }
}
