using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.UseCases.WebPos;

namespace Middleware.Api.Controllers.V1;

/// <summary>
/// Estado de la integración de stock a WEBPOS/HITELL por documento SAP, para que el cliente
/// (Nexus) muestre cada documento como integrado (SYNCED), sin integración aplicable
/// (SKIPPED_NOT_POS) o rechazado (REJECTED, pendiente de reintegración vía los endpoints retry).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webpos/sync-status")]
public sealed class WebPosSyncStatusController : ControllerBase
{
    private readonly GetWebPosSyncStatusUseCase _useCase;

    public WebPosSyncStatusController(GetWebPosSyncStatusUseCase useCase)
    {
        _useCase = useCase;
    }

    /// <summary>Estado de un documento puntual. docType: ST (transferencia) o GR (entrada de mercadería).</summary>
    [HttpGet("{docType}/{docEntry:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string docType, int docEntry, CancellationToken cancellationToken)
    {
        var entry = await _useCase.GetAsync(docType, docEntry, cancellationToken);
        return entry is null
            ? NotFound(new ProblemDetails
            {
                Title = "Sync status not found",
                Detail = $"No hay registro de integración WEBPOS para {docType.ToUpperInvariant()} DocEntry={docEntry}.",
                Status = StatusCodes.Status404NotFound
            })
            : Ok(entry);
    }

    /// <summary>
    /// Lista estados, más reciente primero. Filtros opcionales: status (SYNCED | REJECTED |
    /// SKIPPED_NOT_POS) y docType (ST | GR). Uso típico: ?status=REJECTED para la bandeja de
    /// documentos pendientes de reintegración.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? docType,
        [FromQuery] int top = 100,
        CancellationToken cancellationToken = default)
    {
        var entries = await _useCase.ListAsync(status, docType, top, cancellationToken);
        return Ok(new { entries });
    }
}
