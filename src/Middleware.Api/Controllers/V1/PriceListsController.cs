using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.PriceLists;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/price-lists")]
public sealed class PriceListsController : ControllerBase
{
    private readonly GetPriceListsUseCase _getUseCase;

    public PriceListsController(GetPriceListsUseCase getUseCase)
    {
        _getUseCase = getUseCase;
    }

    /// <summary>Returns the full SAP price list catalog (id + name).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PriceListsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PriceListsResponse>> Get(CancellationToken cancellationToken)
    {
        var result = await _getUseCase.ExecuteAsync(cancellationToken);
        return Ok(result);
    }
}
