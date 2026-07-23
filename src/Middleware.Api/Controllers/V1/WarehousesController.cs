using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.Warehouses;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/warehouses")]
public sealed class WarehousesController : ControllerBase
{
    private readonly GetWarehousesUseCase _getUseCase;

    public WarehousesController(GetWarehousesUseCase getUseCase)
    {
        _getUseCase = getUseCase;
    }

    /// <summary>Returns the full warehouse catalog from SAP.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(WarehousesListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WarehousesListResponse>> Get(CancellationToken cancellationToken)
    {
        var result = await _getUseCase.ExecuteAsync(cancellationToken);
        return Ok(result);
    }
}
