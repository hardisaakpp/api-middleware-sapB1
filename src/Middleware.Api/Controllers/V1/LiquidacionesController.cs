using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.Liquidaciones;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/liquidaciones")]
public sealed class LiquidacionesController : ControllerBase
{
    private readonly CreateLiquidacionUseCase _createUseCase;

    public LiquidacionesController(CreateLiquidacionUseCase createUseCase)
    {
        _createUseCase = createUseCase;
    }

    [HttpPost]
    [ProducesResponseType(typeof(LiquidacionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LiquidacionResponse>> Create(
        [FromBody] CreateLiquidacionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _createUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Create), result);
    }
}
