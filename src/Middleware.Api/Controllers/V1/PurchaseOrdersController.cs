using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.PurchaseOrders;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/purchase-orders")]
public sealed class PurchaseOrdersController : ControllerBase
{
    private readonly CreatePurchaseOrderUseCase _createUseCase;
    private readonly GetPurchaseOrderUseCase _getUseCase;
    private readonly GetPurchaseOrdersUseCase _getListUseCase;
    private readonly UpdatePurchaseOrderUseCase _updateUseCase;

    public PurchaseOrdersController(
        CreatePurchaseOrderUseCase createUseCase,
        GetPurchaseOrderUseCase getUseCase,
        GetPurchaseOrdersUseCase getListUseCase,
        UpdatePurchaseOrderUseCase updateUseCase)
    {
        _createUseCase = createUseCase;
        _getUseCase = getUseCase;
        _getListUseCase = getListUseCase;
        _updateUseCase = updateUseCase;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PurchaseOrdersListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PurchaseOrdersListResponse>> GetList(
        [FromQuery] int top = 20,
        [FromQuery] int skip = 0,
        CancellationToken cancellationToken = default)
    {
        var result = await _getListUseCase.ExecuteAsync(new GetPurchaseOrdersRequest(top, skip), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreatePurchaseOrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreatePurchaseOrderResponse>> Create(
        [FromBody] CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _createUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { docEntry = result.DocEntry }, result);
    }

    [HttpGet("{docEntry:int}")]
    [ProducesResponseType(typeof(PurchaseOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseOrderResponse>> Get(
        int docEntry,
        CancellationToken cancellationToken)
    {
        var result = await _getUseCase.ExecuteAsync(new GetPurchaseOrderRequest(docEntry), cancellationToken);
        if (result is null)
        {
            return NotFound();
        }
        return Ok(result);
    }

    [HttpPatch("{docEntry:int}")]
    [ProducesResponseType(typeof(UpdatePurchaseOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UpdatePurchaseOrderResponse>> Update(
        int docEntry,
        [FromBody] UpdatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        var fullRequest = request with { DocEntry = docEntry };
        var result = await _updateUseCase.ExecuteAsync(fullRequest, cancellationToken);
        return Ok(result);
    }
}
