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
/// Shared by the 3 StockTransfer-creation use cases (Direct/BasedOnDocument/WithBins) to notify
/// WEBPOS right after SAP confirms the transfer. Runs the notification as its own, independently
/// tracked job (own jobId, own retry loop) via <see cref="IJobExecutionCoordinator"/> — never as
/// part of the SAP-creation attempt itself, so a slow/failing WEBPOS can never cause the SAP
/// StockTransfer to be re-created. Every exception is caught here: a WEBPOS failure must never
/// fail the response of a StockTransfer that SAP already accepted.
/// </summary>
public sealed class WebPosStockTransferNotifier : IWebPosStockTransferNotifier
{
    private readonly IJobExecutionCoordinator _jobCoordinator;
    private readonly IPosSyncStatusRepository _posSyncStatusRepository;
    private readonly ILogger<WebPosStockTransferNotifier> _logger;

    public WebPosStockTransferNotifier(IJobExecutionCoordinator jobCoordinator, IPosSyncStatusRepository posSyncStatusRepository, ILogger<WebPosStockTransferNotifier> logger)
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
            await _posSyncStatusRepository.UpsertAsync(PosSyncDocTypes.StockTransfer, docEntry, docNum, "ERROR", Truncate(reason), cancellationToken);
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
        string fromWarehouse,
        string toWarehouse,
        IReadOnlyList<NotifyWebPosStockTransferLine> lines,
        CancellationToken cancellationToken)
    {
        var request = new NotifyWebPosStockTransferRequest(docEntry, docNum, fromWarehouse, toWarehouse, lines);

        try
        {
            var outcome = await _jobCoordinator.ExecuteAsync(
                "NotifyWebPosStockTransfer",
                $"internal /webpos/stock-transfers (DocEntry={docEntry})",
                System.Text.Json.JsonSerializer.Serialize(request, Jobs.JobJsonOptions.Default),
                (sp, ct) => sp.GetRequiredService<NotifyWebPosStockTransferUseCase>().ExecuteAsync(request, ct),
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
            _logger.LogError(ex, "Notificación a WEBPOS falló para DocEntry={DocEntry} — la transferencia en SAP ya está creada y no se ve afectada", docEntry);
            await RecordErrorAsync(docEntry, docNum, ex.Message, cancellationToken);
            return new WebPosSyncInfo(WebPosSyncStatus.Error, ex.Message);
        }
    }
}
