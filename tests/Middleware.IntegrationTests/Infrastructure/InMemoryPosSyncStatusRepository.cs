using System.Collections.Concurrent;
using Middleware.Application.Abstractions;

namespace Middleware.IntegrationTests.Infrastructure;

/// <summary>In-memory stand-in for IPosSyncStatusRepository so WEBPOS tests don't depend on a real SQL Server instance.</summary>
internal sealed class InMemoryPosSyncStatusRepository : IPosSyncStatusRepository
{
    private readonly ConcurrentDictionary<int, (string Status, string? Reason)> _rows = new();

    public IReadOnlyDictionary<int, (string Status, string? Reason)> Rows => _rows;

    public Task UpsertAsync(int docEntry, string status, string? reason, CancellationToken ct = default)
    {
        _rows[docEntry] = (status, reason);
        return Task.CompletedTask;
    }
}
