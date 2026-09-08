using System.Collections.Concurrent;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.Jobs;

namespace Middleware.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory stand-in for IJobQueueRepository so job-queue tests don't depend on a real SQL Server instance.
/// </summary>
internal sealed class InMemoryJobQueueRepository : IJobQueueRepository
{
    private readonly ConcurrentDictionary<Guid, JobRecord> _jobs = new();

    public int Count => _jobs.Count;

    public Task InsertQueuedAsync(Guid jobId, string endpoint, string requestType, string requestPayloadJson, CancellationToken ct = default)
    {
        _jobs[jobId] = new JobRecord(jobId, endpoint, JobStatuses.Encolado, null, null, null, 0, DateTime.UtcNow, null, requestType, requestPayloadJson);
        return Task.CompletedTask;
    }

    public Task<bool> MarkProcessingAsync(Guid jobId, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(jobId, out var job) ||
            job.Status is not (JobStatuses.Encolado or JobStatuses.Procesando))
        {
            return Task.FromResult(false);
        }

        _jobs[jobId] = job with { Status = JobStatuses.Procesando, UpdatedAt = DateTime.UtcNow };
        return Task.FromResult(true);
    }

    public Task MarkCompletedAsync(Guid jobId, int httpStatusCode, string responsePayloadJson, CancellationToken ct = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            _jobs[jobId] = job with
            {
                Status = JobStatuses.Completado,
                HttpStatusCode = httpStatusCode,
                ResultPayloadJson = responsePayloadJson,
                UpdatedAt = DateTime.UtcNow
            };
        }

        return Task.CompletedTask;
    }

    public Task MarkErrorAsync(Guid jobId, string errorMessage, int httpStatusCode, CancellationToken ct = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            _jobs[jobId] = job with
            {
                Status = JobStatuses.Error,
                ErrorMessage = errorMessage,
                HttpStatusCode = httpStatusCode,
                UpdatedAt = DateTime.UtcNow
            };
        }

        return Task.CompletedTask;
    }

    public Task ScheduleRetryAsync(Guid jobId, int attempts, DateTime nextRetryAt, CancellationToken ct = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            _jobs[jobId] = job with { Status = JobStatuses.Encolado, Attempts = attempts, UpdatedAt = DateTime.UtcNow };
        }

        return Task.CompletedTask;
    }

    public Task<JobRecord?> GetByIdAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult(_jobs.TryGetValue(jobId, out var job) ? job : null);

    public Task<PagedResult<JobRecord>> ListAsync(JobQueueFilter filter, CancellationToken ct = default)
    {
        var query = _jobs.Values
            .Where(j => filter.Status is null || j.Status == filter.Status)
            .Where(j => filter.Endpoint is null || j.Endpoint.Contains(filter.Endpoint, StringComparison.OrdinalIgnoreCase))
            .Where(j => filter.From is null || j.CreatedAt >= filter.From)
            .Where(j => filter.To is null || j.CreatedAt <= filter.To)
            .OrderByDescending(j => j.CreatedAt)
            .ToList();

        var items = query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            // Same contract as SQL: list rows never carry the payloads.
            .Select(j => j with { ResultPayloadJson = null, RequestPayloadJson = null })
            .ToList();

        return Task.FromResult(new PagedResult<JobRecord>(items, query.Count, filter.Page, filter.PageSize));
    }

    public Task<IReadOnlyList<OrphanedJob>> GetOrphanedAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrphanedJob>>([]);
}
