using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Middleware.Infrastructure.Sap;

internal sealed class SapSessionWarmupService : IHostedService
{
    private readonly ISapSessionManager _sessionManager;
    private readonly ILogger<SapSessionWarmupService> _logger;

    public SapSessionWarmupService(ISapSessionManager sessionManager, ILogger<SapSessionWarmupService> logger)
    {
        _sessionManager = sessionManager;
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
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
