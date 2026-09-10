using System.Collections.Concurrent;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.IntegrationTests.Infrastructure;

/// <summary>In-memory stand-in for IApiLogRepository so the bitácora endpoints can be tested without SQL Server.</summary>
internal sealed class InMemoryApiLogRepository : IApiLogRepository
{
    private readonly ConcurrentDictionary<long, ApiLogRecord> _rows = new();
    private long _nextId;

    public Task InsertAsync(ApiLogEntry entry, CancellationToken ct = default)
    {
        Seed(entry, DateTime.UtcNow.AddHours(-5));
        return Task.CompletedTask;
    }

    public long Seed(ApiLogEntry entry, DateTime fecha)
    {
        var id = Interlocked.Increment(ref _nextId);
        _rows[id] = new ApiLogRecord(id, entry.Endpoint, entry.Request, entry.Response, fecha, entry.Usuario, entry.StatusCode, entry.DurationMs);
        return id;
    }

    public Task<PagedResult<ApiLogRecord>> ListAsync(ApiLogFilter filter, CancellationToken ct = default)
    {
        var query = _rows.Values
            .Where(r => filter.Endpoint is null || r.Endpoint.Contains(filter.Endpoint, StringComparison.OrdinalIgnoreCase))
            .Where(r => filter.Usuario is null || r.Usuario.Contains(filter.Usuario, StringComparison.OrdinalIgnoreCase))
            .Where(r => filter.StatusCode is null || r.StatusCode == filter.StatusCode)
            .Where(r => !filter.OnlyErrors || r.StatusCode >= 400)
            .Where(r => filter.From is null || r.Fecha >= filter.From)
            .Where(r => filter.To is null || r.Fecha <= filter.To)
            .OrderByDescending(r => r.Fecha)
            .ThenByDescending(r => r.Id)
            .ToList();

        var items = query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(r => r with { Request = null, Response = null })
            .ToList();

        return Task.FromResult(new PagedResult<ApiLogRecord>(items, query.Count, filter.Page, filter.PageSize));
    }

    public Task<ApiLogRecord?> GetByIdAsync(long id, CancellationToken ct = default)
        => Task.FromResult(_rows.TryGetValue(id, out var row) ? row : null);

    public Task<ApiLogDocumentTrace?> FindDocumentTraceAsync(string docType, int docEntry, CancellationToken ct = default)
    {
        var prefix = docType == PosSyncDocTypes.GoodsReceipt ? "POST /api/v1/goods-receipts" : "POST /api/v1/stock-transfers";
        var row = _rows.Values
            .Where(r => r.Endpoint.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && r.StatusCode is >= 200 and < 300
                        && (r.Response ?? string.Empty).Contains($"\"docEntry\":{docEntry},", StringComparison.Ordinal))
            .OrderByDescending(r => r.Id)
            .FirstOrDefault();

        return Task.FromResult(row is null ? null : ApiLogDocumentTraceParser.Parse(docType, row.Request, row.Response));
    }
}
