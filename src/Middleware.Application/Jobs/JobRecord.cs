namespace Middleware.Application.Jobs;

/// <summary>
/// Fila de dbo.job_queue. <c>RequestType</c> y <c>RequestPayloadJson</c> son opcionales porque los
/// listados no los cargan (solo el detalle por id) para no arrastrar payloads grandes.
/// </summary>
public sealed record JobRecord(
    Guid Id,
    string Endpoint,
    string Status,
    int? HttpStatusCode,
    string? ResultPayloadJson,
    string? ErrorMessage,
    int Attempts,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? RequestType = null,
    string? RequestPayloadJson = null
);
