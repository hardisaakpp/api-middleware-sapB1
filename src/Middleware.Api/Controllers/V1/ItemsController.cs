using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.Items;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/items")]
public sealed class ItemsController : ControllerBase
{
    private readonly GetItemUseCase _getItemUseCase;
    private readonly GetItemStockUseCase _getItemStockUseCase;
    private readonly GetItemsBatchUseCase _getItemsBatchUseCase;
    private readonly IValidator<GetItemsBatchRequest> _batchValidator;

    public ItemsController(
        GetItemUseCase getItemUseCase,
        GetItemStockUseCase getItemStockUseCase,
        GetItemsBatchUseCase getItemsBatchUseCase,
        IValidator<GetItemsBatchRequest> batchValidator)
    {
        _getItemUseCase = getItemUseCase;
        _getItemStockUseCase = getItemStockUseCase;
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
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            return ValidationProblem(ModelState);
        }

        var result = await _getItemsBatchUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{itemCode}")]
    [ProducesResponseType(typeof(ItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ItemResponse>> Get(string itemCode, CancellationToken cancellationToken)
    {
        var result = await _getItemUseCase.ExecuteAsync(itemCode, cancellationToken);
        if (result is null)
            return NotFound();
        return Ok(result);
    }

    [HttpGet("{itemCode}/stock/{warehouseCode}")]
    [ProducesResponseType(typeof(ItemStockResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ItemStockResponse>> GetStock(
        string itemCode,
        string warehouseCode,
        CancellationToken cancellationToken)
    {
        var result = await _getItemStockUseCase.ExecuteAsync(itemCode, warehouseCode, cancellationToken);
        if (result is null)
            return NotFound();
        return Ok(result);
    }
}
