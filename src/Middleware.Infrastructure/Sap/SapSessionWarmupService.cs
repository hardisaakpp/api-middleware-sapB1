using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;

namespace Middleware.Infrastructure.Sap;

internal sealed class SapSessionWarmupService : IHostedService
{
    private readonly ISapSessionManager _sessionManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SapSessionWarmupService> _logger;

    public SapSessionWarmupService(
        ISapSessionManager sessionManager,
        IServiceScopeFactory scopeFactory,
        ILogger<SapSessionWarmupService> logger)
    {
        _sessionManager = sessionManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _sessionManager.GetSessionTokenAsync(cancellationToken);
            _logger.LogInformation("SAP session pre-warmed successfully");
        }
        catch (Exception ex)
        {
            // Non-fatal: the first real request will retry the login.
            _logger.LogWarning(ex, "SAP session pre-warm failed — first request will trigger login");
        }

        // Warm the ItemGroups cache in the background so startup is not blocked by paging the
        // full ItemGroups collection (a one-time cost of several seconds on a cold cache).
        // Without this, whichever request triggers the first item lookup after startup (or after
        // the cache TTL expires) pays that cost inline. Fire-and-forget: ISapGateway is scoped,
        // so it runs in its own scope and any failure just leaves the cache cold for a lazy retry.
        _ = Task.Run(WarmItemGroupsAsync, CancellationToken.None);
    }

    private async Task WarmItemGroupsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var gateway = scope.ServiceProvider.GetRequiredService<ISapGateway>();
            await gateway.PrewarmItemGroupsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Non-fatal: the first item lookup will lazily load the ItemGroups map.
            _logger.LogWarning(ex, "SAP ItemGroups pre-warm failed — first item lookup will trigger the load");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
