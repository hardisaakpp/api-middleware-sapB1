using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Abstractions;

namespace Middleware.Api.Controllers.V1;

/// <summary>
/// Bitácora de peticiones al middleware (dbo.api_log, la escribe ApiLogMiddleware): qué llamó
/// cada módulo, con qué cuerpo, qué respondió SAP/el middleware y cuánto tardó. Es la vista de
/// soporte para "qué pasó con la integración X"; el detalle por id trae request y response.
/// Las lecturas de esta misma bitácora no se registran (ver ApiLogMiddleware.SkipPrefixes).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/api-logs")]
public sealed class ApiLogsController : ControllerBase
{
    private const int MaxPageSize = 200;

    private readonly IApiLogRepository _repository;

    public ApiLogsController(IApiLogRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Listado paginado, más reciente primero, sin request/response. Filtros opcionales: endpoint
    /// (contiene, p. ej. "stock-transfers"), usuario (contiene), statusCode (exacto), onlyErrors
    /// (status >= 400), from / to sobre fecha (hora local de Ecuador, UTC-5). pageSize máximo 200.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? endpoint,
        [FromQuery] string? usuario,
        [FromQuery] int? statusCode,
        [FromQuery] bool onlyErrors = false,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var filter = new ApiLogFilter(
            string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim(),
            string.IsNullOrWhiteSpace(usuario) ? null : usuario.Trim(),
            statusCode,
            onlyErrors,
            from,
            to,
            Math.Max(page, 1),
            Math.Clamp(pageSize, 1, MaxPageSize));

        var result = await _repository.ListAsync(filter, cancellationToken);

        return Ok(new
        {
            items = result.Items.Select(ToDto),
            total = result.Total,
            page = result.Page,
            pageSize = result.PageSize,
            totalPages = result.TotalPages
        });
    }

    /// <summary>Detalle de una entrada, con request y response (truncados a 2000 caracteres al registrarse).</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        var entry = await _repository.GetByIdAsync(id, cancellationToken);
        return entry is null ? NotFound() : Ok(ToDto(entry));
    }

    private static object ToDto(ApiLogRecord entry) => new
    {
        id = entry.Id,
        endpoint = entry.Endpoint,
        fecha = entry.Fecha,
        usuario = entry.Usuario,
        statusCode = entry.StatusCode,
        duracionMs = entry.DuracionMs,
        request = entry.Request,
        response = entry.Response
    };
}
