using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Dtos.Responses;
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

    /// <summary>
    /// Estado de un documento puntual. docType: ST (transferencia) o GR (entrada de mercadería).
    /// <c>result</c> es la lectura de negocio (INTEGRADO / NO_CORRESPONDE / RECHAZADO); cuando es
    /// RECHAZADO, <c>rejection</c> indica si el producto no existe en HITELL (PRODUCTO_NO_EXISTE),
    /// los ítems afectados y el endpoint de reintegración.
    /// </summary>
    [HttpGet("{docType}/{docEntry:int}")]
    [ProducesResponseType(typeof(WebPosSyncStatusResponse), StatusCodes.Status200OK)]
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
    /// SKIPPED_NOT_POS), docType (ST | GR), rejectionType (PRODUCTO_NO_EXISTE | OTRO, implica
    /// status=REJECTED) y docEntry (búsqueda puntual desde la pantalla de soporte). Uso típico:
    /// ?rejectionType=PRODUCTO_NO_EXISTE para la lista de productos que hay que crear en HITELL
    /// antes de reintegrar.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? docType,
        [FromQuery] string? rejectionType,
        [FromQuery] int? docEntry,
        [FromQuery] int top = 100,
        CancellationToken cancellationToken = default)
    {
        var entries = await _useCase.ListAsync(status, docType, rejectionType, docEntry, top, cancellationToken);
        return Ok(new { entries });
    }
}
