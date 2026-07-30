using System.Net;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.Jobs;
using Middleware.Application.UseCases.StockTransfer;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/stock-transfers")]
public sealed class StockTransferController : ControllerBase // NOSONAR
{
    private readonly IJobExecutionCoordinator _jobCoordinator;

    public StockTransferController(IJobExecutionCoordinator jobCoordinator)
    {
        _jobCoordinator = jobCoordinator;
    }

    [HttpPost("test")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateTest(
        [FromBody] CreateStockTransferTestRequest request,
        CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "CreateStockTransferTest",
            "POST /api/v1/stock-transfers/test",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<CreateStockTransferTestUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.Created,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<StockTransferResponse> c => CreatedAtAction(nameof(CreateTest), c.Response),
            JobQueued<StockTransferResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPost("requests")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRequest(
        [FromBody] CreateStockTransferRequest request,
        CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "CreateStockTransferRequest",
            "POST /api/v1/stock-transfers/requests",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<CreateStockTransferRequestUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.Created,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<StockTransferResponse> c => CreatedAtAction(nameof(CreateRequest), c.Response),
            JobQueued<StockTransferResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
