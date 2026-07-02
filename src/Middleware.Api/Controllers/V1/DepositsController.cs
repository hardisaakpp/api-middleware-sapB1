using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.Deposits;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/deposits")]
public sealed class DepositsController : ControllerBase
{
    private readonly CreateDepositUseCase _createUseCase;

    public DepositsController(CreateDepositUseCase createUseCase)
    {
        _createUseCase = createUseCase;
    }

    [HttpPost]
    [ProducesResponseType(typeof(DepositResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DepositResponse>> Create(
        [FromBody] CreateDepositRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _createUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Create), result);
    }
}
