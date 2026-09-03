using Microsoft.Extensions.Options;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.WebPos;

internal sealed class WebPosThrottle
{
    private readonly SemaphoreSlim _semaphore;
    private readonly TimeSpan _waitTimeout;

    public WebPosThrottle(IOptions<WebPosSettings> settings)
    {
        var max = settings.Value.MaxConcurrentCalls;
        _semaphore = new SemaphoreSlim(max, max);
        _waitTimeout = TimeSpan.FromSeconds(settings.Value.ThrottleWaitSeconds);
    }

    public async Task WaitAsync(CancellationToken ct)
    {
        var acquired = await _semaphore.WaitAsync(_waitTimeout, ct);
        if (!acquired)
        {
            throw new WebPosThrottleException("WEBPOS está ocupado. Intente de nuevo en unos segundos.");
        }
    }

    public void Release() => _semaphore.Release();
}
