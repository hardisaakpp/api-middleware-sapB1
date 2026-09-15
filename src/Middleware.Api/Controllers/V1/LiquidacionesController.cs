using System.Net;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Api.Idempotency;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.Jobs;
using Middleware.Application.UseCases.Liquidaciones;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/liquidaciones")]
public sealed class LiquidacionesController : ControllerBase
{
    private readonly IJobExecutionCoordinator _jobCoordinator;

    public LiquidacionesController(IJobExecutionCoordinator jobCoordinator)
    {
        _jobCoordinator = jobCoordinator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(LiquidacionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateLiquidacionRequest request,
        CancellationToken cancellationToken)
    {
        // Clave de idempotencia (u_sap_id o Idempotency-Key, o GUID generado): el UDF U_sap_id existe en OPCH.
        var (key, problem) = IdempotencyKeyResolver.Resolve(Request, request.U_sap_id, generateWhenMissing: true);
        if (problem is not null)
        {
            return BadRequest(problem);
        }

        request = request with { U_sap_id = key };
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "CreateLiquidacion",
            "POST /api/v1/liquidaciones",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<CreateLiquidacionUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.Created,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<LiquidacionResponse> c => CreatedAtAction(nameof(Create), c.Response),
            JobQueued<LiquidacionResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
