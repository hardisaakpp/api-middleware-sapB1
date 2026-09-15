using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.WebPos;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webpos/stock-transfers")]
public sealed class WebPosStockTransferController : ControllerBase
{
    private readonly RetryWebPosStockTransferUseCase _useCase;

    public WebPosStockTransferController(RetryWebPosStockTransferUseCase useCase)
    {
        _useCase = useCase;
    }

    /// <summary>
    /// Manually retries the WEBPOS notification for a StockTransfer that already exists in SAP —
    /// used after a business rejection (e.g. "El producto no existe") was corrected directly in
    /// WEBPOS. Never re-creates anything in SAP: rebuilds the payload straight from SAP by
    /// DocEntry and re-runs the same notify-WEBPOS flow used right after creation.
    /// </summary>
    [HttpPost("{docEntry:int}/retry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Retry(int docEntry, CancellationToken cancellationToken)
    {
        var result = await _useCase.ExecuteAsync(new RetryWebPosStockTransferRequest(docEntry), cancellationToken);
        return Ok(result);
    }
}
