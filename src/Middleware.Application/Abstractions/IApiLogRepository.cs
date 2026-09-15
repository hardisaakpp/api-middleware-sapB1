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

/// <summary>
/// Lo que la bitácora sabe de la creación de un documento SAP: el DocNum que devolvió SAP y los
/// almacenes que venían en la petición. Sirve para completar filas viejas de pos_sync_status sin
/// consultar SAP.
/// </summary>
public sealed record ApiLogDocumentTrace(int? DocNum, IReadOnlyList<string> WarehouseCodes);

public interface IApiLogRepository
{
    Task InsertAsync(ApiLogEntry entry, CancellationToken ct = default);

    Task<PagedResult<ApiLogRecord>> ListAsync(ApiLogFilter filter, CancellationToken ct = default);

    Task<ApiLogRecord?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Busca la petición POST que creó el documento (stock-transfers para ST, goods-receipts para GR)
    /// por el <c>docEntry</c> de su respuesta y extrae DocNum y almacenes. Null si no hay rastro.
    /// </summary>
    Task<ApiLogDocumentTrace?> FindDocumentTraceAsync(string docType, int docEntry, CancellationToken ct = default);
}
