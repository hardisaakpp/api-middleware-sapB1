using Middleware.Application.Jobs;

namespace Middleware.Application.Abstractions;

public interface IJobQueueRepository
{
    Task InsertQueuedAsync(Guid jobId, string endpoint, string requestType, string requestPayloadJson, CancellationToken ct = default);
    Task<bool> MarkProcessingAsync(Guid jobId, CancellationToken ct = default);
    Task MarkCompletedAsync(Guid jobId, int httpStatusCode, string responsePayloadJson, CancellationToken ct = default);
    Task MarkErrorAsync(Guid jobId, string errorMessage, int httpStatusCode, CancellationToken ct = default);
    Task ScheduleRetryAsync(Guid jobId, int attempts, DateTime nextRetryAt, CancellationToken ct = default);
    Task<JobRecord?> GetByIdAsync(Guid jobId, CancellationToken ct = default);
    Task<IReadOnlyList<OrphanedJob>> GetOrphanedAsync(CancellationToken ct = default);
}

public sealed record OrphanedJob(Guid Id, string RequestType, string RequestPayloadJson, int Attempts);
