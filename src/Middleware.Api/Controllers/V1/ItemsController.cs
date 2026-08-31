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
using Middleware.Infrastructure.Sap;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/items")]
public sealed class ItemsController : ControllerBase // NOSONAR
{
    private readonly IJobExecutionCoordinator _jobCoordinator;
    private readonly GetItemsBatchUseCase _getItemsBatchUseCase;
    private readonly IValidator<GetItemsBatchRequest> _batchValidator;
    private readonly GetItemsStockBatchUseCase _getItemsStockBatchUseCase;
    private readonly IValidator<GetItemsStockBatchRequest> _stockBatchValidator;
    private readonly GetItemsAvailableStockBatchUseCase _getItemsAvailableStockBatchUseCase;

    public ItemsController(
        IJobExecutionCoordinator jobCoordinator,
        GetItemsBatchUseCase getItemsBatchUseCase,
        IValidator<GetItemsBatchRequest> batchValidator,
        GetItemsStockBatchUseCase getItemsStockBatchUseCase,
        IValidator<GetItemsStockBatchRequest> stockBatchValidator,
        GetItemsAvailableStockBatchUseCase getItemsAvailableStockBatchUseCase)
    {
        _jobCoordinator = jobCoordinator;
        _getItemsBatchUseCase = getItemsBatchUseCase;
        _batchValidator = batchValidator;
        _getItemsStockBatchUseCase = getItemsStockBatchUseCase;
        _stockBatchValidator = stockBatchValidator;
        _getItemsAvailableStockBatchUseCase = getItemsAvailableStockBatchUseCase;
    }

    /// <summary>
    /// Fetches full master data for up to 100 items in a single call. Codes that do not
    /// exist in SAP are listed in notFound; result order is not guaranteed. A Service
    /// Layer connectivity failure fails the whole request (no silent partial results).
    /// </summary>
    [HttpPost("batch")]
    [ProducesResponseType(typeof(ItemsBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ItemsBatchResponse>> GetBatch(
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

        var result = await _getItemsBatchUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Creates a SAP Item master record. Unlike the rest of this API's write endpoints, a SAP
    /// business/validation error (e.g. 400 with SAP's -2035 duplicate ItemCode) is returned with
    /// SAP's original status code rather than the generic 502 GlobalExceptionMiddleware normally
    /// maps SapException to — callers (e.g. api-articulo-maestro's proactive-upsert flow) rely on
    /// seeing the real SAP status to decide how to react. This passthrough only covers the fast,
    /// synchronous attempt below; a failure surfacing later through the job queue (202 Accepted
    /// path) is still reported via the shared JobsController/JobExceptionClassifier mapping.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateItemResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateItemRequest request,
        [FromServices] IValidator<CreateItemRequest> createValidator,
        CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }
            return ValidationProblem(ModelState);
        }

        var payloadJson = JsonSerializer.Serialize(request);

        try
        {
            var outcome = await _jobCoordinator.ExecuteAsync(
                "CreateItem",
                "POST /api/v1/items",
                payloadJson,
                (sp, ct) => sp.GetRequiredService<CreateItemUseCase>().ExecuteAsync(request, ct),
                (int)HttpStatusCode.Created,
                cancellationToken);

            return outcome switch
            {
                JobCompleted<CreateItemResponse> c => CreatedAtAction(nameof(Get), new { itemCode = c.Response.ItemCode }, c.Response),
                JobQueued<CreateItemResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
                _ => StatusCode(StatusCodes.Status500InternalServerError)
            };
        }
        catch (SapException ex)
        {
            return SapErrorResult(ex);
        }
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

    /// <summary>Partially updates a SAP Item master record. See <see cref="Create"/> for the SAP-error passthrough behavior.</summary>
    [HttpPatch("{itemCode}")]
    [ProducesResponseType(typeof(UpdateItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        string itemCode,
        [FromBody] UpdateItemRequest request,
        [FromServices] IValidator<UpdateItemRequest> updateValidator,
        CancellationToken cancellationToken)
    {
        var fullRequest = request with { ItemCode = itemCode };

        var validation = await updateValidator.ValidateAsync(fullRequest, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }
            return ValidationProblem(ModelState);
        }

        var payloadJson = JsonSerializer.Serialize(fullRequest);

        try
        {
            var outcome = await _jobCoordinator.ExecuteAsync(
                "UpdateItem",
                $"PATCH /api/v1/items/{itemCode}",
                payloadJson,
                (sp, ct) => sp.GetRequiredService<UpdateItemUseCase>().ExecuteAsync(fullRequest, ct),
                (int)HttpStatusCode.OK,
                cancellationToken);

            return outcome switch
            {
                JobCompleted<UpdateItemResponse> c => Ok(c.Response),
                JobQueued<UpdateItemResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
                _ => StatusCode(StatusCodes.Status500InternalServerError)
            };
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return NotFound();
        }
        catch (SapException ex)
        {
            return SapErrorResult(ex);
        }
    }

    /// <summary>Deletes a SAP Item master record. See <see cref="Create"/> for the SAP-error passthrough behavior.</summary>
    [HttpDelete("{itemCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string itemCode, CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(new DeleteItemJobRequest(itemCode));

        try
        {
            var outcome = await _jobCoordinator.ExecuteAsync(
                "DeleteItem",
                $"DELETE /api/v1/items/{itemCode}",
                payloadJson,
                (sp, ct) => sp.GetRequiredService<DeleteItemUseCase>().ExecuteAsync(itemCode, ct),
                (int)HttpStatusCode.NoContent,
                cancellationToken);

            return outcome switch
            {
                JobCompleted<DeleteItemResponse> => NoContent(),
                JobQueued<DeleteItemResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
                _ => StatusCode(StatusCodes.Status500InternalServerError)
            };
        }
        catch (SapException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return NotFound();
        }
        catch (SapException ex)
        {
            return SapErrorResult(ex);
        }
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

    [HttpPost("stock/batch")]
    [ProducesResponseType(typeof(ItemsStockBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ItemsStockBatchResponse>> GetStockBatch(
        [FromBody] GetItemsStockBatchRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _stockBatchValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }
            return ValidationProblem(ModelState);
        }

        var result = await _getItemsStockBatchUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("stock/batch/sql")]
    [ProducesResponseType(typeof(ItemsAvailableStockBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ItemsAvailableStockBatchResponse>> GetAvailableStockBatch(
    [FromBody] GetItemsStockBatchRequest request,
    CancellationToken cancellationToken)
    {
        var validation = await _stockBatchValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }
            return ValidationProblem(ModelState);
        }

        var result = await _getItemsAvailableStockBatchUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Atomic item-master balance check used by callers to decide if an item is still editable.
    /// Synchronous by design: callers need the answer before performing an upsert. SAP failures are
    /// passed through with their original status; transport failures are handled by the global pipeline.
    /// </summary>
    [HttpGet("{itemCode}/has-movements")]
    [ProducesResponseType(typeof(HasItemMovementsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<HasItemMovementsResponse>> GetHasMovements(
        [FromRoute] string itemCode,
        [FromServices] HasItemMovementsUseCase hasMovementsUseCase,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await hasMovementsUseCase.ExecuteAsync(itemCode, cancellationToken);
            return Ok(result);
        }
        catch (SapException ex)
        {
            return SapErrorResult(ex);
        }
    }

    // Preserves SAP's original status/body instead of the generic 502 GlobalExceptionMiddleware
    // maps SapException to — see the passthrough note on Create.
    private ObjectResult SapErrorResult(SapException ex)
    {
        var problem = new ProblemDetails
        {
            Status = (int)ex.StatusCode,
            Title = "SAP service error",
            Detail = ex.SapError
        };
        return StatusCode((int)ex.StatusCode, problem);
    }
}
