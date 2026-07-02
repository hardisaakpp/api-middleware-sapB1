using System.Net.Security;
using System.Security.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Infrastructure.Configuration;
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
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, _, _, _) => true, // NOSONAR
#pragma warning disable SYSLIB0039 // TLS 1.0/1.1 deprecated but required for legacy SAP B1 servers
            EnabledSslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12 | SslProtocols.Tls13 // NOSONAR
#pragma warning restore SYSLIB0039
        }
    };

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration
        services.Configure<SapSettings>(configuration.GetSection(SapSettings.SectionName));
        services.Configure<SqlSettings>(configuration.GetSection(SqlSettings.SectionName));

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
        services.AddHostedService<SapSessionWarmupService>();

        // Scoped: one gateway instance per HTTP request
        services.AddScoped<ISapGateway, SapGateway>();

        // SQL
        services.AddScoped<ISqlRepository, SqlRepository>();
        services.AddScoped<IApiLogRepository, ApiLogRepository>();

        // Observability
        services.AddObservability(configuration);

        return services;
    }
}
