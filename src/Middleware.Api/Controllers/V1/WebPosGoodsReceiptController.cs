using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.WebPos;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webpos/goods-receipts")]
public sealed class WebPosGoodsReceiptController : ControllerBase
{
    private readonly RetryWebPosGoodsReceiptUseCase _useCase;

    public WebPosGoodsReceiptController(RetryWebPosGoodsReceiptUseCase useCase)
    {
        _useCase = useCase;
    }

    /// <summary>
    /// Manually retries the WEBPOS notification for a goods receipt (PurchaseDeliveryNote) that
    /// already exists in SAP — used after a business rejection (e.g. "El producto no existe") was
    /// corrected directly in WEBPOS/HITELL. Never re-creates anything in SAP: rebuilds the payload
    /// straight from SAP by DocEntry and re-runs the same notify-WEBPOS flow used right after
    /// creation. Repeatable until the document reaches SYNCED or SKIPPED_NOT_POS.
    /// </summary>
    [HttpPost("{docEntry:int}/retry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Retry(int docEntry, CancellationToken cancellationToken)
    {
        var result = await _useCase.ExecuteAsync(new RetryWebPosGoodsReceiptRequest(docEntry), cancellationToken);
        return Ok(result);
    }
}
