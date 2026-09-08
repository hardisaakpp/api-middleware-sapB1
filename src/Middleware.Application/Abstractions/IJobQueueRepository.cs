using Middleware.Application.Dtos.Responses;
using Middleware.Application.Jobs;

namespace Middleware.Application.Abstractions;

public interface IJobQueueRepository
{
    Task InsertQueuedAsync(Guid jobId, string endpoint, string requestType, string requestPayloadJson, CancellationToken ct = default);
    Task<bool> MarkProcessingAsync(Guid jobId, CancellationToken ct = default);
    Task MarkCompletedAsync(Guid jobId, int httpStatusCode, string responsePayloadJson, CancellationToken ct = default);
    Task MarkErrorAsync(Guid jobId, string errorMessage, int httpStatusCode, CancellationToken ct = default);
    Task ScheduleRetryAsync(Guid jobId, int attempts, DateTime nextRetryAt, CancellationToken ct = default);

    /// <summary>Detalle completo de un job, incluidos el payload enviado y el resultado.</summary>
    Task<JobRecord?> GetByIdAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Listado paginado, más reciente primero, sin los payloads (request/result) para que la
    /// pantalla de soporte pueda recorrer la cola sin arrastrar cuerpos grandes.
    /// </summary>
    Task<PagedResult<JobRecord>> ListAsync(JobQueueFilter filter, CancellationToken ct = default);

    Task<IReadOnlyList<OrphanedJob>> GetOrphanedAsync(CancellationToken ct = default);
}

public sealed record OrphanedJob(Guid Id, string RequestType, string RequestPayloadJson, int Attempts);
