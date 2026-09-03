using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.WebPos;

/// <summary>
/// Manual retry of the WEBPOS notification for a StockTransfer that already exists in SAP —
/// used after a business rejection was corrected directly in WEBPOS. Never re-creates anything
/// in SAP: rebuilds the notification payload by re-reading the StockTransfer from SAP (source of
/// truth for lines/warehouses) and re-computing the current stock, then re-runs the same
/// notify-WEBPOS flow used right after creation.
/// </summary>
public sealed class RetryWebPosStockTransferUseCase : IUseCase<RetryWebPosStockTransferRequest, WebPosSyncInfo>
{
    private readonly ISapGateway _sapGateway;
    private readonly IWebPosStockTransferNotifier _notifier;
    private readonly ILogger<RetryWebPosStockTransferUseCase> _logger;

    public RetryWebPosStockTransferUseCase(ISapGateway sapGateway, IWebPosStockTransferNotifier notifier, ILogger<RetryWebPosStockTransferUseCase> logger)
    {
        _sapGateway = sapGateway;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<WebPosSyncInfo> ExecuteAsync(RetryWebPosStockTransferRequest request, CancellationToken cancellationToken = default)
    {
        var transfer = await _sapGateway.GetStockTransferForTestCompanyAsync(request.DocEntry, cancellationToken)
            ?? throw new KeyNotFoundException($"StockTransfer DocEntry={request.DocEntry} no existe en SAP.");

        _logger.LogInformation("Reintento manual de notificación WEBPOS para DocEntry={DocEntry}", request.DocEntry);

        var lines = transfer.Lines
            .Select(l => new NotifyWebPosStockTransferLine(l.ItemCode, l.Quantity))
            .ToList();

        return await _notifier.NotifyAsync(
            transfer.DocEntry, transfer.DocNum, transfer.FromWarehouse, transfer.ToWarehouse, lines, cancellationToken);
    }
}
