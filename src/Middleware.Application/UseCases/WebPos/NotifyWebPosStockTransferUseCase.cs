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

        var groupsByBusiness = affectedWarehouses.GroupBy(w => w.CodBusiness!);
        var rejectionReasons = new List<string>();

        foreach (var line in request.Lines)
        {
            foreach (var group in groupsByBusiness)
            {
                var storeStock = new List<WebPosStoreStock>();
                foreach (var warehouse in group)
                {
                    var stock = await _sapGateway.GetItemStockForTestCompanyAsync(line.ItemCode, warehouse.WarehouseCode, cancellationToken);
                    storeStock.Add(new WebPosStoreStock(warehouse.WarehouseCode, stock?.InStock ?? 0m));
                }

                var outcome = await _webPosGateway.UpdateInventoryAsync(group.Key, line.ItemCode, storeStock, cancellationToken);

                if (!outcome.Success && IsProductNotFoundRejection(outcome.RejectionReason))
                {
                    outcome = await RetryWithEnrichmentAsync(request.DocEntry, group.Key, line.ItemCode, storeStock, cancellationToken);
                }

                if (!outcome.Success)
                {
                    rejectionReasons.Add($"{line.ItemCode} ({group.Key}): {outcome.RejectionReason}");
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
