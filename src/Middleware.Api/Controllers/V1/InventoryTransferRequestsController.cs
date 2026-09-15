using System.Net;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.Jobs;
using Middleware.Application.Jobs.Handlers;
using Middleware.Application.UseCases.InventoryTransferRequests;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/inventory-transfer-requests")]
public sealed class InventoryTransferRequestsController : ControllerBase
{
    private readonly IJobExecutionCoordinator _jobCoordinator;

    public InventoryTransferRequestsController(IJobExecutionCoordinator jobCoordinator)
    {
        _jobCoordinator = jobCoordinator;
    }

    /// <summary>
    /// Listado paginado, más nuevo primero, JSON crudo de SAP (mismo patrón que
    /// GET /purchase-orders) — para P-008 de api-cedi (sincronizar SOT localmente y armar
    /// grupos). NO requiere commentsContain: antes de esto no había forma de listar "todas
    /// las SOT abiertas", solo buscar por texto puntual en el comentario.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(InventoryTransferRequestsRawListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        [FromQuery] int top = 20,
        [FromQuery] int skip = 0,
        [FromQuery] string? commentsContain = null,
        CancellationToken cancellationToken = default)
    {
        // commentsContain sigue soportado tal cual estaba (compatibilidad hacia atrás) —
        // si viene, se resuelve con el use case de búsqueda por comentario en vez del nuevo
        // listado paginado.
        if (!string.IsNullOrWhiteSpace(commentsContain))
        {
            var commentPayloadJson = JsonSerializer.Serialize(new GetInventoryTransferRequestsByCommentJobRequest(commentsContain));

            var commentOutcome = await _jobCoordinator.ExecuteAsync(
                "GetInventoryTransferRequestsByComment",
                $"GET /api/v1/inventory-transfer-requests?commentsContain={commentsContain}",
                commentPayloadJson,
                (sp, ct) => sp.GetRequiredService<GetInventoryTransferRequestsByCommentUseCase>().ExecuteAsync(commentsContain, ct),
                (int)HttpStatusCode.OK,
                cancellationToken);

            return commentOutcome switch
            {
                JobCompleted<InventoryTransferRequestsListResponse> c => Ok(c.Response),
                JobQueued<InventoryTransferRequestsListResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
                _ => StatusCode(StatusCodes.Status500InternalServerError)
            };
        }

        var request = new GetInventoryTransferRequestsRequest(top, skip);
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetInventoryTransferRequests",
            $"GET /api/v1/inventory-transfer-requests?top={top}&skip={skip}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetInventoryTransferRequestsUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<InventoryTransferRequestsRawListResponse> c => Ok(c.Response),
            JobQueued<InventoryTransferRequestsRawListResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    /// <summary>GET individual por DocEntry, tipado — para "actualizar una SOT puntual" (equivalente al /refresh de ODC).</summary>
    [HttpGet("{docEntry:int}")]
    [ProducesResponseType(typeof(InventoryTransferRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        int docEntry,
        CancellationToken cancellationToken)
    {
        var request = new GetInventoryTransferRequestRequest(docEntry);
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetInventoryTransferRequest",
            $"GET /api/v1/inventory-transfer-requests/{docEntry}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetInventoryTransferRequestUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<InventoryTransferRequestResponse?> c => c.Response is null ? NotFound() : Ok(c.Response),
            JobQueued<InventoryTransferRequestResponse?> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPatch("{docEntry:int}/status")]
    [ProducesResponseType(typeof(UpdateInventoryTransferRequestStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateStatus(
        int docEntry,
        [FromBody] UpdateInventoryTransferRequestStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (request.U_STATUS is not ("0" or "1" or "2"))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "U_STATUS must be '0', '1' or '2'." });
        }

        var fullRequest = request with { DocEntry = docEntry };
        var payloadJson = JsonSerializer.Serialize(fullRequest);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "UpdateInventoryTransferRequestStatus",
            $"PATCH /api/v1/inventory-transfer-requests/{docEntry}/status",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<UpdateInventoryTransferRequestStatusUseCase>().ExecuteAsync(fullRequest, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<UpdateInventoryTransferRequestStatusResponse> c => Ok(c.Response),
            JobQueued<UpdateInventoryTransferRequestStatusResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
