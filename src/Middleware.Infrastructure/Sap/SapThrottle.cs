using Microsoft.Extensions.Options;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.Sap;

internal sealed class SapThrottle
{
    private readonly SemaphoreSlim _semaphore;

    public SapThrottle(IOptions<SapSettings> settings)
    {
        var max = settings.Value.MaxConcurrentSapCalls;
        _semaphore = new SemaphoreSlim(max, max);
    }

    public Task WaitAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);
    public void Release() => _semaphore.Release();
}
