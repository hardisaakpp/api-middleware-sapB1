using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Infrastructure.Sap;

namespace Middleware.IntegrationTests.Infrastructure;

/// <summary>
/// WebApplicationFactory configured for concurrency testing.
/// Replaces the real SAP HTTP client with a fake that has a configurable delay,
/// keeping the real SapGateway and SapThrottle in place to verify throttle behavior.
/// </summary>
internal sealed class SapConcurrencyFactory : WebApplicationFactory<Program>
{
    private readonly TimeSpan _sapCallDelay;

    public FakeSapServiceLayerClient FakeClient { get; private set; } = null!;

    public SapConcurrencyFactory(TimeSpan? sapCallDelay = null)
    {
        _sapCallDelay = sapCallDelay ?? TimeSpan.FromMilliseconds(150);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Override MaxConcurrentSapCalls via IConfiguration so the SapThrottle
        // singleton picks it up at construction time (init-only properties on SapSettings
        // cannot be set via PostConfigure).
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sap:MaxConcurrentSapCalls"] = "2"
            });
        });

        builder.ConfigureServices(services =>
        {
            FakeClient = new FakeSapServiceLayerClient(_sapCallDelay);

            // Replace ISapServiceLayerClient — last registration wins in .NET DI.
            // SapGateway (real, Scoped) and SapThrottle (real, Singleton) stay in place.
            var sapClientDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ISapServiceLayerClient));
            if (sapClientDescriptor is not null)
                services.Remove(sapClientDescriptor);
            services.AddSingleton<ISapServiceLayerClient>(_ => FakeClient);

            // Replace ISapSessionManager with a stub — we are not testing session logic here.
            var sessionDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ISapSessionManager));
            if (sessionDescriptor is not null)
                services.Remove(sessionDescriptor);
            services.AddSingleton<ISapSessionManager>(_ => new StubSessionManager());
        });
    }
}

file sealed class StubSessionManager : ISapSessionManager
{
    public Task<string> GetSessionTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult("fake-session-token");

    public Task InvalidateSessionAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
