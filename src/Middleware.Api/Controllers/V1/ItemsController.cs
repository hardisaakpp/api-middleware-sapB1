using System.Net;
using System.Text.Json;
using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.Jobs;
using Middleware.Application.Jobs.Handlers;
using Middleware.Application.UseCases.Items;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/items")]
public sealed class ItemsController : ControllerBase // NOSONAR
{
    private readonly IJobExecutionCoordinator _jobCoordinator;
    private readonly IValidator<GetItemsBatchRequest> _batchValidator;

    public ItemsController(
        IJobExecutionCoordinator jobCoordinator,
        IValidator<GetItemsBatchRequest> batchValidator)
    {
        _jobCoordinator = jobCoordinator;
        _batchValidator = batchValidator;
    }

    /// <summary>
    /// Fetches full master data for up to 100 items in a single call. Codes that do not
    /// exist in SAP are listed in notFound; result order is not guaranteed. A Service
    /// Layer connectivity failure fails the whole request (no silent partial results).
    /// Follows the same job-queue pattern as the sibling item endpoints: a healthy batch
    /// completes inline and returns 200; if SAP is slow past QueueAfterSeconds or fails
    /// transiently, the work is durably queued and 202 + jobId is returned to poll via
    /// GET /api/v1/jobs/{jobId} (instead of an immediate, non-recoverable 503).
    /// </summary>
    [HttpPost("batch")]
    [ProducesResponseType(typeof(ItemsBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBatch(
        [FromBody] GetItemsBatchRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _batchValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }
            return ValidationProblem(ModelState);
        }

        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetItemsBatch",
            "POST /api/v1/items/batch",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetItemsBatchUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<ItemsBatchResponse> c => Ok(c.Response),
            JobQueued<ItemsBatchResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet("{itemCode}")]
    [ProducesResponseType(typeof(ItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string itemCode, CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(new GetItemJobRequest(itemCode));

        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetItem",
            $"GET /api/v1/items/{itemCode}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetItemUseCase>().ExecuteAsync(itemCode, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<ItemResponse?> c => c.Response is null ? NotFound() : Ok(c.Response),
            JobQueued<ItemResponse?> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet("{itemCode}/stock/{warehouseCode}")]
    [ProducesResponseType(typeof(ItemStockResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStock(
        string itemCode,
        string warehouseCode,
        CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(new GetItemStockJobRequest(itemCode, warehouseCode));

        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetItemStock",
            $"GET /api/v1/items/{itemCode}/stock/{warehouseCode}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetItemStockUseCase>().ExecuteAsync(itemCode, warehouseCode, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<ItemStockResponse?> c => c.Response is null ? NotFound() : Ok(c.Response),
            JobQueued<ItemStockResponse?> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
