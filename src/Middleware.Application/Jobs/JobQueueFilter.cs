namespace Middleware.Application.Jobs;

/// <summary>
/// Filtros del listado de la cola de jobs (GET /jobs). Todos opcionales; <c>Endpoint</c> es
/// búsqueda por contenido (LIKE), las fechas acotan <c>created_at</c> (ambos extremos inclusivos).
/// </summary>
public sealed record JobQueueFilter(
    string? Status,
    string? Endpoint,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize);
