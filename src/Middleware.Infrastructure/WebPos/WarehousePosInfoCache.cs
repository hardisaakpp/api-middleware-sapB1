using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.WebPos;

/// <summary>
/// Singleton in-memory cache of the SAP Warehouses → (U_WEBPOS, U_NEGOCIO) map, keyed by
/// companyDb (StockTransfer creation always runs against Sap:TestCompanyDb, but the cache stays
/// company-aware in case that ever changes). Warehouse POS mapping is master data that rarely
/// changes, so one Service Layer load serves every StockTransfer notification for the TTL. If a
/// refresh fails, the previous (stale) map is served so a transient SAP hiccup never blocks the
/// WEBPOS notification decision.
/// </summary>
public sealed class WarehousePosInfoCache
{
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public WarehousePosInfoCache(IOptions<WebPosSettings> settings)
    {
        _ttl = TimeSpan.FromMinutes(Math.Max(1, settings.Value.WarehousePosInfoCacheMinutes));
    }

    public async Task<IReadOnlyDictionary<string, WarehousePosInfo>> GetOrLoadAsync(
        string companyDb,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, WarehousePosInfo>>> loader,
        CancellationToken ct = default)
    {
        var entry = _entries.GetOrAdd(companyDb, _ => new Entry());

        if (entry.Map is not null && DateTimeOffset.UtcNow < entry.ExpiresAt)
        {
            return entry.Map;
        }

        await entry.Lock.WaitAsync(ct);
        try
        {
            if (entry.Map is not null && DateTimeOffset.UtcNow < entry.ExpiresAt)
            {
                return entry.Map;
            }

            try
            {
                var fresh = await loader(ct);
                entry.Map = fresh;
                entry.ExpiresAt = DateTimeOffset.UtcNow.Add(_ttl);
                return fresh;
            }
            catch when (entry.Map is not null)
            {
                return entry.Map;
            }
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Lock = new(1, 1);
        public IReadOnlyDictionary<string, WarehousePosInfo>? Map;
        public DateTimeOffset ExpiresAt = DateTimeOffset.MinValue;
    }
}
