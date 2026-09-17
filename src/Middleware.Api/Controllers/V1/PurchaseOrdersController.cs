using System.Net;
using System.Text.Json;
using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Middleware.Api.Idempotency;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.Jobs;
using Middleware.Application.UseCases.PurchaseOrders;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/purchase-orders")]
public sealed class PurchaseOrdersController : ControllerBase // NOSONAR
{
    private readonly IJobExecutionCoordinator _jobCoordinator;

    public PurchaseOrdersController(IJobExecutionCoordinator jobCoordinator)
    {
        _jobCoordinator = jobCoordinator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PurchaseOrdersListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        [FromQuery] int top = 20,
        [FromQuery] int skip = 0,
        CancellationToken cancellationToken = default)
    {
        var request = new GetPurchaseOrdersRequest(top, skip);
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetPurchaseOrders",
            $"GET /api/v1/purchase-orders?top={top}&skip={skip}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetPurchaseOrdersUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<PurchaseOrdersListResponse> c => Ok(c.Response),
            JobQueued<PurchaseOrdersListResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreatePurchaseOrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        // Clave de idempotencia (u_sap_id o Idempotency-Key, o GUID generado): el UDF U_sap_id existe en OPOR.
        var (key, problem) = IdempotencyKeyResolver.Resolve(Request, request.U_sap_id, generateWhenMissing: true);
        if (problem is not null)
        {
            return BadRequest(problem);
        }

        request = request with { U_sap_id = key };
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "CreatePurchaseOrder",
            "POST /api/v1/purchase-orders",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<CreatePurchaseOrderUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.Created,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<CreatePurchaseOrderResponse> c => CreatedAtAction(nameof(Get), new { docEntry = c.Response.DocEntry }, c.Response),
            JobQueued<CreatePurchaseOrderResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet("{docEntry:int}")]
    [ProducesResponseType(typeof(PurchaseOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        int docEntry,
        CancellationToken cancellationToken)
    {
        var request = new GetPurchaseOrderRequest(docEntry);
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetPurchaseOrder",
            $"GET /api/v1/purchase-orders/{docEntry}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetPurchaseOrderUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<PurchaseOrderResponse?> c => c.Response is null ? NotFound() : Ok(c.Response),
            JobQueued<PurchaseOrderResponse?> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet("by-doc-num/{docNum:int}")]
    [ProducesResponseType(typeof(PurchaseOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByDocNum(int docNum, CancellationToken cancellationToken)
    {
        var request = new GetPurchaseOrderByDocNumRequest(docNum);
        var payloadJson = JsonSerializer.Serialize(request);
        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetPurchaseOrderByDocNum", $"GET /api/v1/purchase-orders/by-doc-num/{docNum}", payloadJson,
            (sp, ct) => sp.GetRequiredService<GetPurchaseOrderByDocNumUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.OK, cancellationToken);

        return outcome switch
        {
            JobCompleted<PurchaseOrderResponse?> c => c.Response is null ? NotFound() : Ok(c.Response),
            JobQueued<PurchaseOrderResponse?> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet("pendientes")]
    [ProducesResponseType(typeof(IReadOnlyList<PurchaseOrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPendientes(
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta,
        [FromQuery] string warehouseCodes,
        [FromServices] IValidator<GetPurchaseOrdersPendientesRequest> validator,
        CancellationToken cancellationToken = default)
    {
        var codigos = (warehouseCodes ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var request = new GetPurchaseOrdersPendientesRequest(desde, hasta, codigos);
        var validation = await validator.ValidateAsync(request, cancellationToken);
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
            "GetPurchaseOrdersPendientes",
            $"GET /api/v1/purchase-orders/pendientes?desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}&warehouseCodes={warehouseCodes}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetPurchaseOrdersPendientesUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<IReadOnlyList<PurchaseOrderResponse>> c => Ok(c.Response),
            JobQueued<IReadOnlyList<PurchaseOrderResponse>> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPatch("{docEntry:int}")]
    [ProducesResponseType(typeof(UpdatePurchaseOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        int docEntry,
        [FromBody] UpdatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        var fullRequest = request with { DocEntry = docEntry };
        var payloadJson = JsonSerializer.Serialize(fullRequest);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "UpdatePurchaseOrder",
            $"PATCH /api/v1/purchase-orders/{docEntry}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<UpdatePurchaseOrderUseCase>().ExecuteAsync(fullRequest, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<UpdatePurchaseOrderResponse> c => Ok(c.Response),
            JobQueued<UpdatePurchaseOrderResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPatch("{docEntry:int}/status")]
    [ProducesResponseType(typeof(UpdatePurchaseOrderStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateStatus(
        int docEntry,
        [FromBody] UpdatePurchaseOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (request.U_STATUS is not ("0" or "1" or "2"))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "U_STATUS must be '0', '1' or '2'." });
        }

        var fullRequest = request with { DocEntry = docEntry };
        var payloadJson = JsonSerializer.Serialize(fullRequest);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "UpdatePurchaseOrderStatus",
            $"PATCH /api/v1/purchase-orders/{docEntry}/status",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<UpdatePurchaseOrderStatusUseCase>().ExecuteAsync(fullRequest, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<UpdatePurchaseOrderStatusResponse> c => Ok(c.Response),
            JobQueued<UpdatePurchaseOrderStatusResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
