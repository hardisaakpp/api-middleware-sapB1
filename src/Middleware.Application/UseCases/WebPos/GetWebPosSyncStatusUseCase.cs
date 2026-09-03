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

    public async Task<IReadOnlyList<WebPosSyncStatusResponse>> ListAsync(
        string? status, string? docType, string? rejectionType, int top, CancellationToken ct = default)
    {
        string? normalizedType = null;
        if (!string.IsNullOrWhiteSpace(docType))
        {
            normalizedType = NormalizeDocType(docType)
                ?? throw new ArgumentException($"docType inválido: '{docType}'. Valores aceptados: ST, GR.");
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normalizedStatus = Array.Find(ValidStatuses, s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"status inválido: '{status}'. Valores aceptados: SYNCED, REJECTED, SKIPPED_NOT_POS.");
        }

        string? normalizedRejectionType = null;
        if (!string.IsNullOrWhiteSpace(rejectionType))
        {
            normalizedRejectionType = Array.Find(ValidRejectionTypes, t => t.Equals(rejectionType.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"rejectionType inválido: '{rejectionType}'. Valores aceptados: PRODUCTO_NO_EXISTE, OTRO.");
            // A rejection type only makes sense over rejected rows.
            normalizedStatus = "REJECTED";
        }

        var boundedTop = Math.Clamp(top, 1, 500);
        var entries = await _repository.ListAsync(normalizedStatus, normalizedType, boundedTop, ct);

        return entries
            .Select(ToResponse)
            .Where(r => normalizedRejectionType is null || r.Rejection?.Type == normalizedRejectionType)
            .ToList();
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
            entry.DocType, entry.DocEntry, entry.Status, result, entry.Reason,
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
