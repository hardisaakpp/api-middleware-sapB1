using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.InventoryTransferRequests;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/inventory-transfer-requests")]
public sealed class InventoryTransferRequestsController : ControllerBase
{
    private readonly GetInventoryTransferRequestsByCommentUseCase _getByCommentUseCase;

    public InventoryTransferRequestsController(GetInventoryTransferRequestsByCommentUseCase getByCommentUseCase)
    {
        _getByCommentUseCase = getByCommentUseCase;
    }

    [HttpGet]
    [ProducesResponseType(typeof(InventoryTransferRequestsListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<InventoryTransferRequestsListResponse>> GetByComment(
        [FromQuery] string commentsContain,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(commentsContain))
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "commentsContain is required." });

        var result = await _getByCommentUseCase.ExecuteAsync(commentsContain, cancellationToken);
        return Ok(result);
    }
}
