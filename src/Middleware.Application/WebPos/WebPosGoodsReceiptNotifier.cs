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
    private readonly IPosSyncStatusRepository _posSyncStatusRepository;
    private readonly ILogger<WebPosGoodsReceiptNotifier> _logger;

    public WebPosGoodsReceiptNotifier(IJobExecutionCoordinator jobCoordinator, IPosSyncStatusRepository posSyncStatusRepository, ILogger<WebPosGoodsReceiptNotifier> logger)
    {
        _jobCoordinator = jobCoordinator;
        _posSyncStatusRepository = posSyncStatusRepository;
        _logger = logger;
    }

    /// <summary>
    /// A notification that blew up (WEBPOS down, login failed, wrong BaseUrl, timeout) used to leave
    /// no trace in pos_sync_status, so support saw neither "integrado" nor "rechazado" — exactly
    /// what happened in production on 2026-09-09. Recording ERROR makes it visible and retryable.
    /// </summary>
    private async Task RecordErrorAsync(int docEntry, int docNum, string reason, CancellationToken cancellationToken)
    {
        try
        {
            await _posSyncStatusRepository.UpsertAsync(PosSyncDocTypes.GoodsReceipt, docEntry, docNum, "ERROR", Truncate(reason), [], cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo registrar el estado ERROR de WEBPOS para DocEntry={DocEntry}", docEntry);
        }
    }

    private static string Truncate(string value) => value.Length <= 900 ? value : value[..900] + "…";

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
            await RecordErrorAsync(docEntry, docNum, ex.Message, cancellationToken);
            return new WebPosSyncInfo(WebPosSyncStatus.Error, ex.Message);
        }
    }
}
