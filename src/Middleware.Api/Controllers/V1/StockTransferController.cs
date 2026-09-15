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
using Middleware.Application.UseCases.StockTransfer;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/stock-transfers")]
public sealed class StockTransferController : ControllerBase // NOSONAR
{
    private readonly IJobExecutionCoordinator _jobCoordinator;

    public StockTransferController(IJobExecutionCoordinator jobCoordinator)
    {
        _jobCoordinator = jobCoordinator;
    }

    /// <summary>
    /// Igual que /based-on-document pero corre contra Sap:CompanyDb (la compañía real, no el
    /// sandbox de WebPOS al que apuntan /based-on-document, /direct y /with-bins) — para
    /// transferencias de bodega ajenas a WebPOS, ej. Distribución de api-cedi (P-011). No
    /// dispara la notificación WebPOS.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateStockTransferBasedOnDocumentRequest request,
        CancellationToken cancellationToken)
    {
        // Con GUID de respaldo: OWTR comparte el UDF de documentos de marketing; si en Sap:CompanyDb
        // faltara, el gateway crea sin clave y lo registra como error.
        var (key, problem) = IdempotencyKeyResolver.Resolve(Request, request.U_sap_id, generateWhenMissing: true);
        if (problem is not null)
        {
            return BadRequest(problem);
        }

        request = request with { U_sap_id = key };
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "CreateStockTransferForCompany",
            "POST /api/v1/stock-transfers",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<CreateStockTransferForCompanyUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.Created,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<StockTransferResponse> c => CreatedAtAction(nameof(Create), c.Response),
            JobQueued<StockTransferResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPost("based-on-document")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateBasedOnDocument(
        [FromBody] CreateStockTransferBasedOnDocumentRequest request,
        CancellationToken cancellationToken)
    {
        // La clave viaja dentro del payload encolado para que TODOS los reintentos del job (y la
        // recuperación tras un reinicio) usen la misma y no creen un segundo documento. Aquí sí se
        // genera un GUID si el módulo no envía clave: el UDF U_sap_id existe en OWTR (TestCompanyDb).
        var (key, problem) = IdempotencyKeyResolver.Resolve(Request, request.U_sap_id, generateWhenMissing: true);
        if (problem is not null)
        {
            return BadRequest(problem);
        }

        request = request with { U_sap_id = key };
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "CreateStockTransferBasedOnDocument",
            "POST /api/v1/stock-transfers/based-on-document",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<CreateStockTransferBasedOnDocumentUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.Created,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<StockTransferResponse> c => CreatedAtAction(nameof(CreateBasedOnDocument), c.Response),
            JobQueued<StockTransferResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPost("direct")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateDirect(
        [FromBody] CreateStockTransferDirectRequest request,
        CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "CreateStockTransferDirect",
            "POST /api/v1/stock-transfers/direct",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<CreateStockTransferDirectUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.Created,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<StockTransferResponse> c => CreatedAtAction(nameof(CreateDirect), c.Response),
            JobQueued<StockTransferResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPost("with-bins")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateWithBins(
        [FromBody] CreateStockTransferWithBinsRequest request,
        CancellationToken cancellationToken)
    {
        var (key, problem) = IdempotencyKeyResolver.Resolve(Request, request.U_sap_id, generateWhenMissing: true);
        if (problem is not null)
        {
            return BadRequest(problem);
        }

        request = request with { U_sap_id = key };
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "CreateStockTransferWithBins",
            "POST /api/v1/stock-transfers/with-bins",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<CreateStockTransferWithBinsUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.Created,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<StockTransferResponse> c => CreatedAtAction(nameof(CreateWithBins), c.Response),
            JobQueued<StockTransferResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPost("requests")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRequest(
        [FromBody] CreateStockTransferRequest request,
        CancellationToken cancellationToken)
    {
        // Con GUID de respaldo: el UDF U_sap_id existe en OWTQ (60, confirmado 14/09/2026 en prueba y
        // producción), así que los reintentos de la cola quedan protegidos aunque el módulo no envíe clave.
        var (key, problem) = IdempotencyKeyResolver.Resolve(Request, request.U_sap_id, generateWhenMissing: true);
        if (problem is not null)
        {
            return BadRequest(problem);
        }

        request = request with { U_sap_id = key };
        var payloadJson = JsonSerializer.Serialize(request);

        var outcome = await _jobCoordinator.ExecuteAsync(
            "CreateStockTransferRequest",
            "POST /api/v1/stock-transfers/requests",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<CreateStockTransferRequestUseCase>().ExecuteAsync(request, ct),
            (int)HttpStatusCode.Created,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<StockTransferResponse> c => CreatedAtAction(nameof(CreateRequest), c.Response),
            JobQueued<StockTransferResponse> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet("by-correlation/{uSapId}")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetByCorrelationId(
    string uSapId,
    [FromServices] GetStockTransferByCorrelationIdUseCase useCase,
    CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(uSapId))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "uSapId is required." });
        }

        var result = await useCase.ExecuteAsync(uSapId, cancellationToken);

        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{docEntry:int}/status")]
    [ProducesResponseType(typeof(StockTransferStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(
    int docEntry,
    [FromServices] GetStockTransferStatusUseCase useCase,
    CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(docEntry, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }


}
