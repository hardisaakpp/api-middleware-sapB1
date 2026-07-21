using System.Net;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.Jobs;
using Middleware.Application.Jobs.Handlers;
using Middleware.Application.UseCases.BusinessPartners;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/business-partners")]
public sealed class BusinessPartnersController : ControllerBase
{
    private readonly IJobExecutionCoordinator _jobCoordinator;

    public BusinessPartnersController(IJobExecutionCoordinator jobCoordinator)
    {
        _jobCoordinator = jobCoordinator;
    }

    [HttpGet("{cardCode}")]
    [ProducesResponseType(typeof(BusinessPartnerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string cardCode, CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(new GetBusinessPartnerJobRequest(cardCode));

        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetBusinessPartner",
            $"GET /api/v1/business-partners/{cardCode}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetBusinessPartnerUseCase>().ExecuteAsync(cardCode, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<BusinessPartnerResponse?> c => c.Response is null ? NotFound() : Ok(c.Response),
            JobQueued<BusinessPartnerResponse?> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
