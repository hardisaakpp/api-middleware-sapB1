using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Domain.Enums;

namespace Middleware.Application.UseCases.WebPos;

/// <summary>
/// Notifies WEBPOS of the item/store stock affected by a SAP goods receipt
/// (PurchaseDeliveryNote) that already succeeded. Mirrors
/// <see cref="NotifyWebPosStockTransferUseCase"/> with two differences: the receiving warehouse
/// comes per line (not from a document-level from/to pair), and every SAP read goes against the
/// default company (Sap:CompanyDb) — the company goods receipts are created in — instead of the
/// test company the stock-transfer flow uses.
/// </summary>
public sealed class NotifyWebPosGoodsReceiptUseCase : IUseCase<NotifyWebPosGoodsReceiptRequest, WebPosSyncInfo>
{
    private const string ProductNotFoundMessage = "El producto no existe.";

    private readonly ISapGateway _sapGateway;
    private readonly IWebPosGateway _webPosGateway;
    private readonly IPosSyncStatusRepository _posSyncStatusRepository;
    private readonly ILogger<NotifyWebPosGoodsReceiptUseCase> _logger;

    public NotifyWebPosGoodsReceiptUseCase(
        ISapGateway sapGateway,
        IWebPosGateway webPosGateway,
        IPosSyncStatusRepository posSyncStatusRepository,
        ILogger<NotifyWebPosGoodsReceiptUseCase> logger)
    {
        _sapGateway = sapGateway;
        _webPosGateway = webPosGateway;
        _posSyncStatusRepository = posSyncStatusRepository;
        _logger = logger;
    }

    public async Task<WebPosSyncInfo> ExecuteAsync(NotifyWebPosGoodsReceiptRequest request, CancellationToken cancellationToken = default)
    {
        var warehouseCodes = request.Lines.Select(l => l.WarehouseCode).Distinct().ToList();
        var posInfo = await _sapGateway.GetWarehousesPosInfoAsync(warehouseCodes, cancellationToken);

        bool IsPosWarehouse(string code) =>
            posInfo.GetValueOrDefault(code) is { IsPosEnabled: true } info && !string.IsNullOrEmpty(info.CodBusiness);

        var affectedLines = request.Lines.Where(l => IsPosWarehouse(l.WarehouseCode)).ToList();

        if (affectedLines.Count == 0)
        {
            _logger.LogInformation(
                "GoodsReceipt DocEntry={DocEntry}: ninguna bodega receptora está en WEBPOS (U_WEBPOS) — se omite la notificación",
                request.DocEntry);
            await _posSyncStatusRepository.UpsertAsync(PosSyncDocTypes.GoodsReceipt, request.DocEntry, "SKIPPED_NOT_POS", null, cancellationToken);
            return new WebPosSyncInfo(WebPosSyncStatus.SkippedNotPos);
        }

        var rejectionReasons = new List<string>();

        // Una entrada puede recibir el mismo ítem en varias bodegas: se agrupa por ítem y, dentro
        // del ítem, por negocio WEBPOS — una llamada a WEBPOS por (ítem, negocio) con el stock
        // absoluto de cada bodega involucrada, igual que el flujo de transferencias.
        foreach (var itemGroup in affectedLines.GroupBy(l => l.ItemCode))
        {
            var businessGroups = itemGroup
                .Select(l => posInfo[l.WarehouseCode])
                .DistinctBy(w => w.WarehouseCode)
                .GroupBy(w => w.CodBusiness!);

            foreach (var group in businessGroups)
            {
                var storeStock = new List<WebPosStoreStock>();
                foreach (var warehouse in group)
                {
                    var stock = await _sapGateway.GetItemStockAsync(itemGroup.Key, warehouse.WarehouseCode, cancellationToken);
                    storeStock.Add(new WebPosStoreStock(warehouse.WarehouseCode, stock?.InStock ?? 0m));
                }

                var outcome = await _webPosGateway.UpdateInventoryAsync(group.Key, itemGroup.Key, storeStock, cancellationToken);

                if (!outcome.Success && IsProductNotFoundRejection(outcome.RejectionReason))
                {
                    outcome = await RetryWithEnrichmentAsync(request.DocEntry, group.Key, itemGroup.Key, storeStock, cancellationToken);
                }

                if (!outcome.Success)
                {
                    rejectionReasons.Add($"{itemGroup.Key} ({group.Key}): {outcome.RejectionReason}");
                }
            }
        }

        if (rejectionReasons.Count > 0)
        {
            var reason = string.Join(" | ", rejectionReasons);
            _logger.LogWarning(
                "GoodsReceipt DocEntry={DocEntry}: WEBPOS rechazó {Count} actualización(es) — {Reason}",
                request.DocEntry, rejectionReasons.Count, reason);
            await _posSyncStatusRepository.UpsertAsync(PosSyncDocTypes.GoodsReceipt, request.DocEntry, "REJECTED", reason, cancellationToken);
            return new WebPosSyncInfo(WebPosSyncStatus.Rejected, reason);
        }

        _logger.LogInformation("GoodsReceipt DocEntry={DocEntry}: stock sincronizado en WEBPOS correctamente", request.DocEntry);
        await _posSyncStatusRepository.UpsertAsync(PosSyncDocTypes.GoodsReceipt, request.DocEntry, "SYNCED", null, cancellationToken);
        return new WebPosSyncInfo(WebPosSyncStatus.Synced);
    }

    private static bool IsProductNotFoundRejection(string? reason) =>
        reason is not null && reason.Trim().Equals(ProductNotFoundMessage, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Same one-shot auto-create retry as the stock-transfer flow (see
    /// <see cref="NotifyWebPosStockTransferUseCase"/>), reading the enrichment from the default
    /// company where the goods receipt lives.
    /// </summary>
    private async Task<WebPosUpdateOutcome> RetryWithEnrichmentAsync(
        int docEntry, string codBusiness, string itemCode, IReadOnlyList<WebPosStoreStock> storeStock, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "GoodsReceipt DocEntry={DocEntry}: WEBPOS no tiene el ítem {ItemCode} en el negocio {CodBusiness} — reintentando con datos completos",
            docEntry, itemCode, codBusiness);

        var enrichment = await _sapGateway.GetItemPosEnrichmentAsync(itemCode, cancellationToken);
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
