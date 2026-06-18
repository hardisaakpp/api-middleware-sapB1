using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.UseCases.BusinessPartners;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/business-partners")]
public sealed class BusinessPartnersController : ControllerBase
{
    private readonly GetBusinessPartnerUseCase _getUseCase;

    public BusinessPartnersController(GetBusinessPartnerUseCase getUseCase)
    {
        _getUseCase = getUseCase;
    }

    [HttpGet("{cardCode}")]
    [ProducesResponseType(typeof(BusinessPartnerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BusinessPartnerResponse>> Get(string cardCode, CancellationToken cancellationToken)
    {
        var result = await _getUseCase.ExecuteAsync(cardCode, cancellationToken);
        if (result is null)
            return NotFound();
        return Ok(result);
    }
}
