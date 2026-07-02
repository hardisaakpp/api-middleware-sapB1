using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.Items;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/items")]
public sealed class ItemsController : ControllerBase // NOSONAR
{
    private readonly GetItemUseCase _getItemUseCase;
    private readonly GetItemStockUseCase _getItemStockUseCase;

    public ItemsController(GetItemUseCase getItemUseCase, GetItemStockUseCase getItemStockUseCase)
    {
        _getItemUseCase = getItemUseCase;
        _getItemStockUseCase = getItemStockUseCase;
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
