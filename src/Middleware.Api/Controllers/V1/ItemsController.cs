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
    private readonly GetItemsBatchUseCase _getItemsBatchUseCase;
    private readonly IValidator<GetItemsBatchRequest> _batchValidator;

    public ItemsController(
        IJobExecutionCoordinator jobCoordinator,
        GetItemsBatchUseCase getItemsBatchUseCase,
        IValidator<GetItemsBatchRequest> batchValidator)
    {
        _jobCoordinator = jobCoordinator;
        _getItemsBatchUseCase = getItemsBatchUseCase;
        _batchValidator = batchValidator;
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
    /// Busca ítems por ItemCode o BarCode (contains, no exacto) — para P-012 de api-cedi
    /// ("Consulta Stock"). Ruta separada de GET /{itemCode} para no confundir "buscar" con
    /// "traer por clave exacta".
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<ItemSearchResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] string query, [FromQuery] int top = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "query is required." });
        }

        top = Math.Clamp(top, 1, 100);
        var payloadJson = JsonSerializer.Serialize(new SearchItemsJobRequest(query, top));

        var outcome = await _jobCoordinator.ExecuteAsync(
            "SearchItems",
            $"GET /api/v1/items/search?query={query}&top={top}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<SearchItemsUseCase>().ExecuteAsync(query, top, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<IReadOnlyList<ItemSearchResult>> c => Ok(c.Response),
            JobQueued<IReadOnlyList<ItemSearchResult>> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
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

    /// <summary>
    /// Stock del ítem en TODOS los almacenes de una vez (solo los que tienen InStock o
    /// Committed distinto de cero) — para P-012 de api-cedi ("mostrar por ubicación").
    /// Evita N llamadas (una por almacén) a GetStock.
    /// </summary>
    [HttpGet("{itemCode}/stock")]
    [ProducesResponseType(typeof(IReadOnlyList<ItemStockResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStockAllWarehouses(string itemCode, CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(new GetItemStockAllWarehousesJobRequest(itemCode));

        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetItemStockAllWarehouses",
            $"GET /api/v1/items/{itemCode}/stock",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetItemStockAllWarehousesUseCase>().ExecuteAsync(itemCode, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<IReadOnlyList<ItemStockResponse>?> c => c.Response is null ? NotFound() : Ok(c.Response),
            JobQueued<IReadOnlyList<ItemStockResponse>?> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
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
