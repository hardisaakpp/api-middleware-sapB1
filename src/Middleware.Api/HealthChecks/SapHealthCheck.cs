using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Middleware.Infrastructure.Sap;

namespace Middleware.Api.HealthChecks;

public sealed class SapHealthCheck : IHealthCheck
{
    private readonly ISapSessionManager _sessionManager;
    private readonly ILogger<SapHealthCheck> _logger;

    public SapHealthCheck(ISapSessionManager sessionManager, ILogger<SapHealthCheck> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validates that a SAP session can be established or is already cached.
            // Avoids a full business-layer round-trip on every probe.
            await _sessionManager.GetSessionTokenAsync(cancellationToken);
            return HealthCheckResult.Healthy("SAP Service Layer is reachable.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SAP health check failed");
            return HealthCheckResult.Unhealthy("SAP Service Layer is not reachable.", ex);
        }
    }
}
