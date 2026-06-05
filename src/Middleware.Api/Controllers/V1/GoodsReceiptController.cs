using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.GoodsReceipt;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/goods-receipts")]
public sealed class GoodsReceiptController : ControllerBase
{
    private readonly CreateGoodsReceiptUseCase _createUseCase;

    public GoodsReceiptController(CreateGoodsReceiptUseCase createUseCase)
    {
        _createUseCase = createUseCase;
    }

    [HttpPost]
    [ProducesResponseType(typeof(GoodsReceiptResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GoodsReceiptResponse>> Create(
        [FromBody] CreateGoodsReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _createUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Create), result);
    }
}
