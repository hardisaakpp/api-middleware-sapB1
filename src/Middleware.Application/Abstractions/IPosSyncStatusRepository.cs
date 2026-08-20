namespace Middleware.Application.Abstractions;

public interface IPosSyncStatusRepository
{
    Task UpsertAsync(int docEntry, string status, string? reason, CancellationToken ct = default);
}
