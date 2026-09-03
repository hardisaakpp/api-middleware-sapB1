using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.WebPos;
using Middleware.Domain.Enums;

namespace Middleware.Application.WebPos;

/// <summary>
/// Same contract as <see cref="WebPosStockTransferNotifier"/> but for goods receipts
/// (PurchaseDeliveryNotes): notifies WEBPOS right after SAP confirms the receipt, as its own
/// independently tracked job, and never lets a WEBPOS failure fail the response of a goods
/// receipt SAP already accepted.
/// </summary>
public sealed class WebPosGoodsReceiptNotifier : IWebPosGoodsReceiptNotifier
{
    private readonly IJobExecutionCoordinator _jobCoordinator;
    private readonly ILogger<WebPosGoodsReceiptNotifier> _logger;

    public WebPosGoodsReceiptNotifier(IJobExecutionCoordinator jobCoordinator, ILogger<WebPosGoodsReceiptNotifier> logger)
    {
        _jobCoordinator = jobCoordinator;
        _logger = logger;
    }

    public async Task<WebPosSyncInfo> NotifyAsync(
        int docEntry,
        int docNum,
        IReadOnlyList<NotifyWebPosGoodsReceiptLine> lines,
        CancellationToken cancellationToken)
    {
        var request = new NotifyWebPosGoodsReceiptRequest(docEntry, docNum, lines);

        try
        {
            var outcome = await _jobCoordinator.ExecuteAsync(
                "NotifyWebPosGoodsReceipt",
                $"internal /webpos/goods-receipts (DocEntry={docEntry})",
                System.Text.Json.JsonSerializer.Serialize(request, Jobs.JobJsonOptions.Default),
                (sp, ct) => sp.GetRequiredService<NotifyWebPosGoodsReceiptUseCase>().ExecuteAsync(request, ct),
                (int)HttpStatusCode.OK,
                cancellationToken);

            return outcome switch
            {
                JobCompleted<WebPosSyncInfo> completed => completed.Response,
                JobQueued<WebPosSyncInfo> queued => new WebPosSyncInfo(WebPosSyncStatus.Queued, JobId: queued.JobId),
                _ => new WebPosSyncInfo(WebPosSyncStatus.Error, "Resultado de job inesperado")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notificación a WEBPOS falló para GoodsReceipt DocEntry={DocEntry} — la entrada en SAP ya está creada y no se ve afectada", docEntry);
            return new WebPosSyncInfo(WebPosSyncStatus.Error, ex.Message);
        }
    }
}
