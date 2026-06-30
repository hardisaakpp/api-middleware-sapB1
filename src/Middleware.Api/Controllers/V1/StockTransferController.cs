using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.StockTransfer;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/stock-transfers")]
public sealed class StockTransferController : ControllerBase // NOSONAR
{
    private readonly CreateStockTransferUseCase _createUseCase;
    private readonly CreateStockTransferWithBinsUseCase _createWithBinsUseCase;
    private readonly CreateStockTransferRequestUseCase _createRequestUseCase;

    public StockTransferController(
        CreateStockTransferUseCase createUseCase,
        CreateStockTransferWithBinsUseCase createWithBinsUseCase,
        CreateStockTransferRequestUseCase createRequestUseCase)
    {
        _createUseCase = createUseCase;
        _createWithBinsUseCase = createWithBinsUseCase;
        _createRequestUseCase = createRequestUseCase;
    }

    [HttpPost]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StockTransferResponse>> Create(
        [FromBody] CreateStockTransferRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _createUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Create), result);
    }

    [HttpPost("with-bins")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StockTransferResponse>> CreateWithBins(
        [FromBody] CreateStockTransferWithBinsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _createWithBinsUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(CreateWithBins), result);
    }

    [HttpPost("requests")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StockTransferResponse>> CreateRequest(
        [FromBody] CreateStockTransferRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _createRequestUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(CreateRequest), result);
    }
}
