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
    private readonly ILogger<WebPosStockTransferNotifier> _logger;

    public WebPosStockTransferNotifier(IJobExecutionCoordinator jobCoordinator, ILogger<WebPosStockTransferNotifier> logger)
    {
        _jobCoordinator = jobCoordinator;
        _logger = logger;
    }

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
            return new WebPosSyncInfo(WebPosSyncStatus.Error, ex.Message);
        }
    }
}
