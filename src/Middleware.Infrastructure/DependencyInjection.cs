using System.Net.Security;
using System.Security.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Infrastructure.Configuration;
using Middleware.Infrastructure.Jobs;
using Middleware.Infrastructure.Observability;
using Middleware.Infrastructure.Resilience;
using Middleware.Infrastructure.Sap;
using Middleware.Infrastructure.Sql;

namespace Middleware.Infrastructure;

public static class DependencyInjection
{
    // SAP B1 Service Layer uses a self-signed certificate and may negotiate older TLS versions.
    // SocketsHttpHandler gives us full control over TLS on .NET 8 (WinHttp-based HttpClientHandler
    // does not honour RemoteCertificateValidationCallback for handshake-level failures).
    private static SocketsHttpHandler BuildSapHandler() => new()
    {
        // SAP es una IP fija en la LAN (ej. http://10.10.41.4:50001), nunca se alcanza vía proxy.
        // Sin esta línea, SocketsHttpHandler usa el default UseProxy=true → cae al proxy del
        // sistema Windows (WinHTTP/WinINET), que incluye "Detectar la configuración
        // automáticamente" (WPAD). El sondeo WPAD por DNS/DHCP se cuelga ~15-30s por CADA
        // conexión nueva antes de resolver "sin proxy para este host" — la causa de la lentitud
        // (login ~17s, llamadas 20-30s, timeouts de Polly a 30s). El cliente Python directo no
        // hace WPAD (solo respeta HTTP_PROXY) y por eso responde en ~0.3s contra el mismo SAP.
        UseProxy = false,
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, _, _, _) => true, // NOSONAR
#pragma warning disable SYSLIB0039 // TLS 1.0/1.1 deprecated but required for legacy SAP B1 servers
            EnabledSslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12 | SslProtocols.Tls13 // NOSONAR
#pragma warning restore SYSLIB0039
        },
        // SAP Service Layer's own HTTP server closes idle keep-alive connections sooner than
        // .NET's default pool (2 min) expects, so a pooled connection can already be dead
        // server-side by the time it's reused — the reuse then fails with
        // "HttpIOException: The response ended prematurely" and Polly has to retry with a
        // fresh connection, adding a full retry-backoff delay to that request. Evicting idle
        // connections from the pool sooner, and rotating connections periodically, makes the
        // client far less likely to attempt reusing an already-closed socket.
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(45),
        PooledConnectionLifetime = TimeSpan.FromMinutes(3)
    };

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration
        services.Configure<SapSettings>(configuration.GetSection(SapSettings.SectionName));
        services.Configure<SqlSettings>(configuration.GetSection(SqlSettings.SectionName));
        services.Configure<JobQueueSettings>(configuration.GetSection(JobQueueSettings.SectionName));

        services.AddHttpClient("SapLogin", (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<SapSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + '/'); // NOSONAR
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(BuildSapHandler);

        // Typed HttpClient for all other SAP calls, with full resilience pipeline
        services.AddHttpClient<ISapServiceLayerClient, SapServiceLayerClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<SapSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + '/'); // NOSONAR
            // Outer timeout > inner pipeline timeout so the pipeline's timeout policy fires first
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds + 10);
        })
        .ConfigurePrimaryHttpMessageHandler(BuildSapHandler)
        .AddSapResiliencePipeline();

        // Singletons: shared across all requests
        services.AddSingleton<ISapSessionManager, SapSessionManager>();
        services.AddSingleton<SapThrottle>();
        services.AddSingleton<ItemGroupsCache>();
        services.AddHostedService<SapSessionWarmupService>();

        // Scoped: one gateway instance per HTTP request
        services.AddScoped<ISapGateway, SapGateway>();

        // SQL
        services.AddScoped<ISqlRepository, SqlRepository>();
        services.AddScoped<IApiLogRepository, ApiLogRepository>();
        services.AddScoped<IJobQueueRepository, JobQueueRepository>();

        // Job queue (durable async fallback for slow/saturated SAP calls)
        services.AddScoped<JobRunner>();
        services.AddScoped<IJobExecutionCoordinator, JobExecutionCoordinator>();
        services.AddHostedService<JobQueueRecoveryService>();

        // Observability
        services.AddObservability(configuration);

        return services;
    }
}
