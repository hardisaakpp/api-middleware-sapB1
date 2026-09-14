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
using Middleware.Application.UseCases.GoodsReceipt;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/goods-receipts")]
public sealed class GoodsReceiptController : ControllerBase
{
    private readonly IJobExecutionCoordinator _jobCoordinator;

    public GoodsReceiptController(IJobExecutionCoordinator jobCoordinator)
    {
        _jobCoordinator = jobCoordinator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(GoodsReceiptResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateGoodsReceiptRequest request,
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
            "CreateGoodsReceipt",
            "POST /api/v1/goods-receipts",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<CreateGoodsReceiptUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.Created,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<GoodsReceiptResponse> c => CreatedAtAction(nameof(Create), c.Response),
            JobQueued<GoodsReceiptResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet]
    [ProducesResponseType(typeof(GoodsReceiptsByBaseEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetByBaseEntry(
        [FromQuery] int baseEntry,
        [FromQuery] string vendorCode,
        CancellationToken cancellationToken)
    {
        var request = new GetGoodsReceiptsByBaseEntryRequest(baseEntry, vendorCode);
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetGoodsReceiptsByBaseEntry",
            $"GET /api/v1/goods-receipts?baseEntry={baseEntry}&vendorCode={vendorCode}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetGoodsReceiptsByBaseEntryUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<GoodsReceiptsByBaseEntryResponse> c => Ok(c.Response),
            JobQueued<GoodsReceiptsByBaseEntryResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
