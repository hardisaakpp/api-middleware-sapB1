using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.Abstractions;

public record ApiLogEntry(
    string Endpoint,
    string? Request,
    string? Response,
    string Usuario,
    int StatusCode,
    int DurationMs);

/// <summary>
/// Fila de dbo.api_log. <c>Fecha</c> se guarda en hora local de Ecuador (UTC-5), no en UTC.
/// En los listados <c>Request</c> y <c>Response</c> vienen null; solo el detalle por id los carga.
/// </summary>
public sealed record ApiLogRecord(
    long Id,
    string Endpoint,
    string? Request,
    string? Response,
    DateTime Fecha,
    string Usuario,
    int StatusCode,
    long DuracionMs);

/// <summary>
/// Filtros de la bitácora (GET /api-logs). <c>Endpoint</c> y <c>Usuario</c> son búsqueda por
/// contenido; <c>OnlyErrors</c> deja solo status_code >= 400; las fechas acotan <c>fecha</c>.
/// </summary>
public sealed record ApiLogFilter(
    string? Endpoint,
    string? Usuario,
    int? StatusCode,
    bool OnlyErrors,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize);

public interface IApiLogRepository
{
    Task InsertAsync(ApiLogEntry entry, CancellationToken ct = default);

    Task<PagedResult<ApiLogRecord>> ListAsync(ApiLogFilter filter, CancellationToken ct = default);

    Task<ApiLogRecord?> GetByIdAsync(long id, CancellationToken ct = default);
}
