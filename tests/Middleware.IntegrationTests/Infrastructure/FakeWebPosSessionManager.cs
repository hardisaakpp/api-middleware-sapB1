using Middleware.Infrastructure.WebPos;

namespace Middleware.IntegrationTests.Infrastructure;

/// <summary>Fake WEBPOS session manager — returns a fixed token instantly, no real login call.</summary>
internal sealed class FakeWebPosSessionManager : IWebPosSessionManager
{
    public Task<string> GetTokenAsync(CancellationToken ct = default) => Task.FromResult("fake-token");

    public Task InvalidateAsync(CancellationToken ct = default) => Task.CompletedTask;
}
