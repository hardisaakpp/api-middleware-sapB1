using System.Collections.Concurrent;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.IntegrationTests.Infrastructure;

/// <summary>In-memory stand-in for IPosSyncStatusRepository so WEBPOS tests don't depend on a real SQL Server instance.</summary>
internal sealed class InMemoryPosSyncStatusRepository : IPosSyncStatusRepository
{
    private readonly ConcurrentDictionary<(string DocType, int DocEntry), PosSyncStatusEntry> _rows = new();

    public IReadOnlyDictionary<(string DocType, int DocEntry), PosSyncStatusEntry> Rows => _rows;

    public Task UpsertAsync(string docType, int docEntry, int? docNum, string status, string? reason, CancellationToken ct = default)
    {
        _rows.AddOrUpdate(
            (docType, docEntry),
            _ => new PosSyncStatusEntry(docType, docEntry, docNum, status, reason, 1, DateTime.UtcNow, null),
            (_, prev) => prev with { Status = status, Reason = reason, DocNum = docNum ?? prev.DocNum, Attempts = prev.Attempts + 1, UpdatedAt = DateTime.UtcNow });
        return Task.CompletedTask;
    }

    public Task<PosSyncStatusEntry?> GetAsync(string docType, int docEntry, CancellationToken ct = default)
        => Task.FromResult(_rows.TryGetValue((docType, docEntry), out var entry) ? entry : null);

    public Task<PagedResult<PosSyncStatusEntry>> ListAsync(PosSyncStatusFilter filter, CancellationToken ct = default)
    {
        var query = _rows.Values
            .Where(e => filter.Status is null || e.Status == filter.Status)
            .Where(e => filter.DocType is null || e.DocType == filter.DocType)
            .Where(e => filter.DocEntry is null || e.DocEntry == filter.DocEntry)
            .Where(e => filter.DocNum is null || e.DocNum == filter.DocNum)
            .Where(e => filter.From is null || (e.UpdatedAt ?? e.CreatedAt) >= filter.From)
            .Where(e => filter.To is null || (e.UpdatedAt ?? e.CreatedAt) <= filter.To)
            .OrderByDescending(e => e.UpdatedAt ?? e.CreatedAt)
            .ToList();

        var items = query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
        return Task.FromResult(new PagedResult<PosSyncStatusEntry>(items, query.Count, filter.Page, filter.PageSize));
    }
}
