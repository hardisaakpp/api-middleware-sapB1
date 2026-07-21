using Microsoft.Extensions.Options;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.Sap;

/// <summary>
/// Singleton in-memory cache of the SAP ItemGroups map (Number → GroupName).
/// Item groups are few and rarely change, so one Service Layer load serves all
/// item requests for the TTL. If a refresh fails, the previous (stale) map is
/// served so item lookups never fail because of group-name enrichment.
/// </summary>
public sealed class ItemGroupsCache
{
    private readonly TimeSpan _ttl;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyDictionary<int, string>? _map;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public ItemGroupsCache(IOptions<SapSettings> settings)
    {
        _ttl = TimeSpan.FromMinutes(Math.Max(1, settings.Value.ItemGroupsCacheMinutes));
    }

    public async Task<IReadOnlyDictionary<int, string>> GetOrLoadAsync(
        Func<CancellationToken, Task<IReadOnlyDictionary<int, string>>> loader,
        CancellationToken ct = default)
    {
        var cached = _map;
        if (cached is not null && DateTimeOffset.UtcNow < _expiresAt)
            return cached;

        await _lock.WaitAsync(ct);
        try
        {
            cached = _map;
            if (cached is not null && DateTimeOffset.UtcNow < _expiresAt)
                return cached;

            try
            {
                var fresh = await loader(ct);
                _map = fresh;
                _expiresAt = DateTimeOffset.UtcNow.Add(_ttl);
                return fresh;
            }
            catch when (cached is not null)
            {
                // Serve stale on refresh failure; next request retries the load
                return cached;
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
