using System.Collections.Concurrent;
using Middleware.Application.Abstractions;

namespace Middleware.IntegrationTests.Infrastructure;

/// <summary>In-memory stand-in for IPosSyncStatusRepository so WEBPOS tests don't depend on a real SQL Server instance.</summary>
internal sealed class InMemoryPosSyncStatusRepository : IPosSyncStatusRepository
{
    private readonly ConcurrentDictionary<(string DocType, int DocEntry), PosSyncStatusEntry> _rows = new();

    public IReadOnlyDictionary<(string DocType, int DocEntry), PosSyncStatusEntry> Rows => _rows;

    public Task UpsertAsync(string docType, int docEntry, string status, string? reason, CancellationToken ct = default)
    {
        _rows.AddOrUpdate(
            (docType, docEntry),
            _ => new PosSyncStatusEntry(docType, docEntry, status, reason, 1, DateTime.UtcNow, null),
            (_, prev) => prev with { Status = status, Reason = reason, Attempts = prev.Attempts + 1, UpdatedAt = DateTime.UtcNow });
        return Task.CompletedTask;
    }

    public Task<PosSyncStatusEntry?> GetAsync(string docType, int docEntry, CancellationToken ct = default)
        => Task.FromResult(_rows.TryGetValue((docType, docEntry), out var entry) ? entry : null);

    public Task<IReadOnlyList<PosSyncStatusEntry>> ListAsync(string? status, string? docType, int top, CancellationToken ct = default)
    {
        IReadOnlyList<PosSyncStatusEntry> result = _rows.Values
            .Where(e => (status is null || e.Status == status) && (docType is null || e.DocType == docType))
            .OrderByDescending(e => e.UpdatedAt ?? e.CreatedAt)
            .Take(top)
            .ToList();
        return Task.FromResult(result);
    }
}
