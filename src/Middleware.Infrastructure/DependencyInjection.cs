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
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration
        services.Configure<SapSettings>(configuration.GetSection(SapSettings.SectionName));
        services.Configure<SqlSettings>(configuration.GetSection(SqlSettings.SectionName));

        // Named HttpClient for SAP login (used by the Singleton SapSessionManager)
        services.AddHttpClient("SapLogin", (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<SapSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        });

        // Typed HttpClient for all other SAP calls, with full resilience pipeline
        services.AddHttpClient<ISapServiceLayerClient, SapServiceLayerClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<SapSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            // Outer timeout > inner pipeline timeout so the pipeline's timeout policy fires first
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds + 10);
        })
        .AddSapResiliencePipeline();

        // Singletons: shared across all requests
        services.AddSingleton<ISapSessionManager, SapSessionManager>();
        services.AddSingleton<SapThrottle>();
        services.AddHostedService<SapSessionWarmupService>();

        // Scoped: one gateway instance per HTTP request
        services.AddScoped<ISapGateway, SapGateway>();

        // SQL
        services.AddScoped<ISqlRepository, SqlRepository>();

        // Observability
        services.AddObservability(configuration);

        return services;
    }
}
