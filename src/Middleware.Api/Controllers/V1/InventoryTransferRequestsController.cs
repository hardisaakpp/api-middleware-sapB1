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

    [HttpGet]
    [ProducesResponseType(typeof(InventoryTransferRequestsListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetByComment(
        [FromQuery] string commentsContain,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(commentsContain))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "commentsContain is required." });
        }

        var payloadJson = JsonSerializer.Serialize(new GetInventoryTransferRequestsByCommentJobRequest(commentsContain));

        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetInventoryTransferRequestsByComment",
            $"GET /api/v1/inventory-transfer-requests?commentsContain={commentsContain}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetInventoryTransferRequestsByCommentUseCase>().ExecuteAsync(commentsContain, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<InventoryTransferRequestsListResponse> c => Ok(c.Response),
            JobQueued<InventoryTransferRequestsListResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
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
