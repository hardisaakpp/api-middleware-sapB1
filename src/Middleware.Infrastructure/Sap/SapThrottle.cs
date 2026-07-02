using Microsoft.Extensions.Options;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.Sap;

internal sealed class SapThrottle
{
    private readonly SemaphoreSlim _semaphore;
    private readonly TimeSpan _waitTimeout;

    public SapThrottle(IOptions<SapSettings> settings)
    {
        var max = settings.Value.MaxConcurrentSapCalls;
        _semaphore = new SemaphoreSlim(max, max);
        _waitTimeout = TimeSpan.FromSeconds(settings.Value.ThrottleWaitSeconds);
    }

    public async Task WaitAsync(CancellationToken ct)
    {
        var acquired = await _semaphore.WaitAsync(_waitTimeout, ct);
        if (!acquired)
            throw new SapThrottleException("El servidor SAP está ocupado. Intente de nuevo en unos segundos.");
    }

    public void Release() => _semaphore.Release();
}
