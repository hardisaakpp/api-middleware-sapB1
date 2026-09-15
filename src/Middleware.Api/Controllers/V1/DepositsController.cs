using System.Net;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Api.Idempotency;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.Jobs;
using Middleware.Application.UseCases.Deposits;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/deposits")]
public sealed class DepositsController : ControllerBase
{
    private readonly IJobExecutionCoordinator _jobCoordinator;

    public DepositsController(IJobExecutionCoordinator jobCoordinator)
    {
        _jobCoordinator = jobCoordinator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(DepositResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateDepositRequest request,
        CancellationToken cancellationToken)
    {
        // Clave de idempotencia opcional (u_sap_id o Idempotency-Key); si no viene se genera un GUID:
        // el UDF U_sap_id existe en los documentos de marketing (OIGN/OPOR confirmados 14/09/2026) y,
        // si en alguna compañía faltara, el gateway crea sin clave y lo registra como error.
        var (key, problem) = IdempotencyKeyResolver.Resolve(Request, request.U_sap_id, generateWhenMissing: true);
        if (problem is not null)
        {
            return BadRequest(problem);
        }

        request = request with { U_sap_id = key };
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "CreateDeposit",
            "POST /api/v1/deposits",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<CreateDepositUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.Created,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<DepositResponse> c => CreatedAtAction(nameof(Create), c.Response),
            JobQueued<DepositResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
