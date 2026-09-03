using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.WebPos;

/// <summary>
/// Manual retry of the WEBPOS notification for a goods receipt (PurchaseDeliveryNote) that
/// already exists in SAP — used after a business rejection (e.g. the item was just created in
/// WEBPOS/HITELL) to re-run the integration until it lands in SYNCED or SKIPPED_NOT_POS. Never
/// re-creates anything in SAP: rebuilds the payload by re-reading the receipt and re-computing
/// the current stock.
/// </summary>
public sealed class RetryWebPosGoodsReceiptUseCase : IUseCase<RetryWebPosGoodsReceiptRequest, WebPosSyncInfo>
{
    private readonly ISapGateway _sapGateway;
    private readonly IWebPosGoodsReceiptNotifier _notifier;
    private readonly ILogger<RetryWebPosGoodsReceiptUseCase> _logger;

    public RetryWebPosGoodsReceiptUseCase(ISapGateway sapGateway, IWebPosGoodsReceiptNotifier notifier, ILogger<RetryWebPosGoodsReceiptUseCase> logger)
    {
        _sapGateway = sapGateway;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<WebPosSyncInfo> ExecuteAsync(RetryWebPosGoodsReceiptRequest request, CancellationToken cancellationToken = default)
    {
        var receipt = await _sapGateway.GetGoodsReceiptAsync(request.DocEntry, cancellationToken)
            ?? throw new KeyNotFoundException($"GoodsReceipt DocEntry={request.DocEntry} no existe en SAP.");

        _logger.LogInformation("Reintento manual de notificación WEBPOS para GoodsReceipt DocEntry={DocEntry}", request.DocEntry);

        var lines = receipt.Lines
            .Select(l => new NotifyWebPosGoodsReceiptLine(l.ItemCode, l.Quantity, l.WarehouseCode))
            .ToList();

        return await _notifier.NotifyAsync(receipt.DocEntry, receipt.DocNum, lines, cancellationToken);
    }
}
