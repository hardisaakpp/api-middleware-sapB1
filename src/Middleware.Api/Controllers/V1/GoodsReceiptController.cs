using System.Net;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.Jobs;
using Middleware.Application.UseCases.GoodsReceipt;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/goods-receipts")]
public sealed class GoodsReceiptController : ControllerBase
{
    private readonly IJobExecutionCoordinator _jobCoordinator;

    public GoodsReceiptController(IJobExecutionCoordinator jobCoordinator)
    {
        _jobCoordinator = jobCoordinator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(GoodsReceiptResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateGoodsReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "CreateGoodsReceipt",
            "POST /api/v1/goods-receipts",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<CreateGoodsReceiptUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.Created,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<GoodsReceiptResponse> c => CreatedAtAction(nameof(Create), c.Response),
            JobQueued<GoodsReceiptResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
