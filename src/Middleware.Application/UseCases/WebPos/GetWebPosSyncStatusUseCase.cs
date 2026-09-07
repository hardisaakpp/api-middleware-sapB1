using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.WebPos;

namespace Middleware.Application.UseCases.WebPos;

/// <summary>
/// Read-only lookups over pos_sync_status so clients (Nexus) can show the integration state of
/// each document — INTEGRADO (SYNCED), NO_CORRESPONDE (SKIPPED_NOT_POS), RECHAZADO (REJECTED) —
/// and drive the re-integration of REJECTED ones via the retry endpoints. Every entry is returned
/// already interpreted (see <see cref="WebPosSyncStatusResponse"/>) so the UI never has to parse
/// HITELL's raw messages.
/// </summary>
public sealed class GetWebPosSyncStatusUseCase
{
    public const int MaxPageSize = 200;

    /// <summary>
    /// El tipo de rechazo se deduce del texto de HITELL, no está en una columna: para filtrar por él
    /// se traen los rechazados del rango (acotado) y se pagina en memoria.
    /// </summary>
    private const int MaxRejectedScanned = 2000;

    private static readonly string[] ValidDocTypes = [PosSyncDocTypes.StockTransfer, PosSyncDocTypes.GoodsReceipt];
    private static readonly string[] ValidStatuses = ["SYNCED", "REJECTED", "SKIPPED_NOT_POS"];
    private static readonly string[] ValidRejectionTypes = [WebPosRejectionTypes.ProductNotFound, WebPosRejectionTypes.Other];

    private readonly IPosSyncStatusRepository _repository;

    public GetWebPosSyncStatusUseCase(IPosSyncStatusRepository repository)
    {
        _repository = repository;
    }

    public async Task<WebPosSyncStatusResponse?> GetAsync(string docType, int docEntry, CancellationToken ct = default)
    {
        var normalized = NormalizeDocType(docType)
            ?? throw new ArgumentException($"docType inválido: '{docType}'. Valores aceptados: ST, GR.");

        var entry = await _repository.GetAsync(normalized, docEntry, ct);
        return entry is null ? null : ToResponse(entry);
    }

    public async Task<PagedResult<WebPosSyncStatusResponse>> ListAsync(
        WebPosSyncStatusQuery query, CancellationToken ct = default)
    {
        string? normalizedType = null;
        if (!string.IsNullOrWhiteSpace(query.DocType))
        {
            normalizedType = NormalizeDocType(query.DocType)
                ?? throw new ArgumentException($"docType inválido: '{query.DocType}'. Valores aceptados: ST, GR.");
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            normalizedStatus = Array.Find(ValidStatuses, s => s.Equals(query.Status.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"status inválido: '{query.Status}'. Valores aceptados: SYNCED, REJECTED, SKIPPED_NOT_POS.");
        }

        string? normalizedRejectionType = null;
        if (!string.IsNullOrWhiteSpace(query.RejectionType))
        {
            normalizedRejectionType = Array.Find(ValidRejectionTypes, t => t.Equals(query.RejectionType.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"rejectionType inválido: '{query.RejectionType}'. Valores aceptados: PRODUCTO_NO_EXISTE, OTRO.");
            // A rejection type only makes sense over rejected rows.
            normalizedStatus = "REJECTED";
        }

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        if (normalizedRejectionType is null)
        {
            var result = await _repository.ListAsync(
                new PosSyncStatusFilter(normalizedStatus, normalizedType, query.DocEntry, query.DocNum, query.From, query.To, page, pageSize), ct);
            return new PagedResult<WebPosSyncStatusResponse>(result.Items.Select(ToResponse).ToList(), result.Total, page, pageSize);
        }

        var rejected = await _repository.ListAsync(
            new PosSyncStatusFilter(normalizedStatus, normalizedType, query.DocEntry, query.DocNum, query.From, query.To, 1, MaxRejectedScanned), ct);
        var matching = rejected.Items
            .Select(ToResponse)
            .Where(r => r.Rejection?.Type == normalizedRejectionType)
            .ToList();

        return new PagedResult<WebPosSyncStatusResponse>(
            matching.Skip((page - 1) * pageSize).Take(pageSize).ToList(), matching.Count, page, pageSize);
    }

    private static string? NormalizeDocType(string docType) =>
        Array.Find(ValidDocTypes, t => t.Equals(docType.Trim(), StringComparison.OrdinalIgnoreCase));

    public static WebPosSyncStatusResponse ToResponse(PosSyncStatusEntry entry)
    {
        var result = entry.Status switch
        {
            "SYNCED" => WebPosIntegrationResults.Integrated,
            "SKIPPED_NOT_POS" => WebPosIntegrationResults.NotApplicable,
            _ => WebPosIntegrationResults.Rejected
        };

        var rejection = result == WebPosIntegrationResults.Rejected ? BuildRejection(entry) : null;

        return new WebPosSyncStatusResponse(
            entry.DocType, entry.DocEntry, entry.DocNum, entry.Status, result, entry.Reason,
            entry.Attempts, entry.CreatedAt, entry.UpdatedAt, rejection);
    }

    private static WebPosRejectionDetail BuildRejection(PosSyncStatusEntry entry)
    {
        var items = WebPosRejectionClassifier.ParseItems(entry.Reason);
        var type = WebPosRejectionClassifier.ClassifyReason(entry.Reason);

        var description = type == WebPosRejectionTypes.ProductNotFound
            ? $"Producto no creado en HITELL: {string.Join(", ", items.Select(i => i.ItemCode ?? "?").Distinct())}. Crear el producto en HITELL y reintegrar."
            : "HITELL rechazó la actualización de stock. Revisar el mensaje por ítem, corregir en HITELL y reintegrar.";

        var resource = entry.DocType == PosSyncDocTypes.GoodsReceipt ? "goods-receipts" : "stock-transfers";
        return new WebPosRejectionDetail(type, description, items, $"/api/v1/webpos/{resource}/{entry.DocEntry}/retry");
    }
}

/// <summary>Parámetros del listado GET /webpos/sync-status, tal como llegan del cliente (sin validar).</summary>
public sealed record WebPosSyncStatusQuery(
    string? Status,
    string? DocType,
    string? RejectionType,
    int? DocEntry,
    int? DocNum,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize);
