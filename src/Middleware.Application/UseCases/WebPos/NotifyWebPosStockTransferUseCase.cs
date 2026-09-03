using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Domain.Enums;

namespace Middleware.Application.UseCases.WebPos;

/// <summary>
/// Notifies WEBPOS of the item/store stock affected by a SAP StockTransfer that already
/// succeeded. Never re-touches SAP — only reads (post-transfer stock, U_WEBPOS/U_NEGOCIO) and
/// pushes the resulting absolute stock to WEBPOS.
/// </summary>
public sealed class NotifyWebPosStockTransferUseCase : IUseCase<NotifyWebPosStockTransferRequest, WebPosSyncInfo>
{
    // Exact WEBPOS rejection message for "this item/store pair doesn't exist yet in WEBPOS" —
    // the only rejection worth auto-remediating; the other 3 (missing params, store not in
    // business, missing stock/item) are payload/config problems a retry can't fix on its own.
    private const string ProductNotFoundMessage = "El producto no existe.";

    private readonly ISapGateway _sapGateway;
    private readonly IWebPosGateway _webPosGateway;
    private readonly IPosSyncStatusRepository _posSyncStatusRepository;
    private readonly ILogger<NotifyWebPosStockTransferUseCase> _logger;

    public NotifyWebPosStockTransferUseCase(
        ISapGateway sapGateway,
        IWebPosGateway webPosGateway,
        IPosSyncStatusRepository posSyncStatusRepository,
        ILogger<NotifyWebPosStockTransferUseCase> logger)
    {
        _sapGateway = sapGateway;
        _webPosGateway = webPosGateway;
        _posSyncStatusRepository = posSyncStatusRepository;
        _logger = logger;
    }

    public async Task<WebPosSyncInfo> ExecuteAsync(NotifyWebPosStockTransferRequest request, CancellationToken cancellationToken = default)
    {
        var warehouseCodes = new[] { request.FromWarehouse, request.ToWarehouse }.Distinct().ToList();
        var posInfo = await _sapGateway.GetWarehousesPosInfoForTestCompanyAsync(warehouseCodes, cancellationToken);

        var affectedWarehouses = warehouseCodes
            .Select(code => posInfo.GetValueOrDefault(code))
            .Where(info => info is not null && info.IsPosEnabled && !string.IsNullOrEmpty(info.CodBusiness))
            .Select(info => info!)
            .ToList();

        if (affectedWarehouses.Count == 0)
        {
            _logger.LogInformation(
                "DocEntry={DocEntry}: ni {FromWarehouse} ni {ToWarehouse} están en WEBPOS (U_WEBPOS) — se omite la notificación",
                request.DocEntry, request.FromWarehouse, request.ToWarehouse);
            await _posSyncStatusRepository.UpsertAsync(PosSyncDocTypes.StockTransfer, request.DocEntry, "SKIPPED_NOT_POS", null, cancellationToken);
            return new WebPosSyncInfo(WebPosSyncStatus.SkippedNotPos);
        }

        var groupsByBusiness = affectedWarehouses.GroupBy(w => w.CodBusiness!).ToList();
        var rejectionReasons = new List<string>();

        foreach (var itemCode in request.Lines.Select(l => l.ItemCode))
        {
            foreach (var group in groupsByBusiness)
            {
                var rejection = await NotifyItemForBusinessAsync(request.DocEntry, itemCode, group.Key, group, cancellationToken);
                if (rejection is not null)
                {
                    rejectionReasons.Add(rejection);
                }
            }
        }

        if (rejectionReasons.Count > 0)
        {
            var reason = string.Join(" | ", rejectionReasons);
            _logger.LogWarning(
                "DocEntry={DocEntry}: WEBPOS rechazó {Count} actualización(es) — {Reason}",
                request.DocEntry, rejectionReasons.Count, reason);
            await _posSyncStatusRepository.UpsertAsync(PosSyncDocTypes.StockTransfer, request.DocEntry, "REJECTED", reason, cancellationToken);
            return new WebPosSyncInfo(WebPosSyncStatus.Rejected, reason);
        }

        _logger.LogInformation("DocEntry={DocEntry}: stock sincronizado en WEBPOS correctamente", request.DocEntry);
        await _posSyncStatusRepository.UpsertAsync(PosSyncDocTypes.StockTransfer, request.DocEntry, "SYNCED", null, cancellationToken);
        return new WebPosSyncInfo(WebPosSyncStatus.Synced);
    }

    /// <summary>
    /// One WEBPOS call per (item, business) with the absolute post-transfer stock of every
    /// warehouse of that business. Returns null when WEBPOS accepted, otherwise the rejection
    /// text to report for the document.
    /// </summary>
    private async Task<string?> NotifyItemForBusinessAsync(
        int docEntry, string itemCode, string codBusiness, IEnumerable<WarehousePosInfo> warehouses, CancellationToken cancellationToken)
    {
        var storeStock = await BuildStoreStockAsync(itemCode, warehouses, cancellationToken);
        var outcome = await _webPosGateway.UpdateInventoryAsync(codBusiness, itemCode, storeStock, cancellationToken);

        if (!outcome.Success && IsProductNotFoundRejection(outcome.RejectionReason))
        {
            outcome = await RetryWithEnrichmentAsync(docEntry, codBusiness, itemCode, storeStock, cancellationToken);
        }

        return outcome.Success ? null : $"{itemCode} ({codBusiness}): {outcome.RejectionReason}";
    }

    private async Task<List<WebPosStoreStock>> BuildStoreStockAsync(
        string itemCode, IEnumerable<WarehousePosInfo> warehouses, CancellationToken cancellationToken)
    {
        var storeStock = new List<WebPosStoreStock>();
        foreach (var warehouseCode in warehouses.Select(w => w.WarehouseCode))
        {
            var stock = await _sapGateway.GetItemStockForTestCompanyAsync(itemCode, warehouseCode, cancellationToken);
            storeStock.Add(new WebPosStoreStock(warehouseCode, stock?.InStock ?? 0m));
        }

        return storeStock;
    }

    private static bool IsProductNotFoundRejection(string? reason) =>
        reason is not null && reason.Trim().Equals(ProductNotFoundMessage, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One-shot retry for "El producto no existe.": pulls price/brand/tax/barcodes from SAP and
    /// resends the same store_stock with those fields filled in, so WEBPOS can auto-create the
    /// item/store pair instead of requiring someone to do it by hand. If SAP has no enrichment
    /// data, or WEBPOS still rejects afterwards (for any reason), the original/second rejection
    /// is what the caller reports — this never loops more than once.
    /// </summary>
    private async Task<WebPosUpdateOutcome> RetryWithEnrichmentAsync(
        int docEntry, string codBusiness, string itemCode, IReadOnlyList<WebPosStoreStock> storeStock, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "DocEntry={DocEntry}: WEBPOS no tiene el ítem {ItemCode} en el negocio {CodBusiness} — reintentando con datos completos",
            docEntry, itemCode, codBusiness);

        var enrichment = await _sapGateway.GetItemPosEnrichmentForTestCompanyAsync(itemCode, cancellationToken);
        if (enrichment is null)
        {
            return new WebPosUpdateOutcome(false, ProductNotFoundMessage);
        }

        var enrichedStoreStock = storeStock
            .Select(s => s with
            {
                Price = enrichment.Price,
                Brand = enrichment.Brand,
                CodeTax = enrichment.CodeTax,
                Barcodes = enrichment.Barcodes.Count > 0 ? enrichment.Barcodes : null
            })
            .ToList();

        return await _webPosGateway.UpdateInventoryAsync(codBusiness, itemCode, enrichedStoreStock, cancellationToken);
    }
}
