using System.Text.Json.Nodes;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Abstractions;
using Middleware.Application.Jobs;

namespace Middleware.Api.Controllers.V1;

/// <summary>
/// Cola durable de peticiones a SAP (dbo.job_queue): consulta puntual para el cliente que recibió
/// un 202 y listado paginado para la pantalla de soporte de integraciones en Nexus.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/jobs")]
public sealed class JobsController : ControllerBase
{
    private const int MaxPageSize = 200;
    private static readonly string[] ValidStatuses =
        [JobStatuses.Encolado, JobStatuses.Procesando, JobStatuses.Completado, JobStatuses.Error];

    private readonly IJobQueueRepository _repository;

    public JobsController(IJobQueueRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("{jobId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _repository.GetByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        return Ok(ToDto(job));
    }

    /// <summary>
    /// Listado paginado, más reciente primero, sin payloads (usar GET /jobs/{jobId} para el detalle).
    /// Filtros opcionales: status (ENCOLADO | PROCESANDO | COMPLETADO | ERROR), endpoint (contiene),
    /// from / to (sobre createdAt, inclusivos). pageSize máximo 200.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? endpoint,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normalizedStatus = Array.Find(ValidStatuses, s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (normalizedStatus is null)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid request",
                    Detail = $"status inválido: '{status}'. Valores aceptados: {string.Join(", ", ValidStatuses)}.",
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        var filter = new JobQueueFilter(
            normalizedStatus,
            string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim(),
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

    private static object ToDto(JobRecord job) => new
    {
        jobId = job.Id,
        endpoint = job.Endpoint,
        requestType = job.RequestType,
        status = job.Status,
        httpStatusCode = job.HttpStatusCode,
        request = ParseJson(job.RequestPayloadJson),
        result = ParseJson(job.ResultPayloadJson),
        error = job.ErrorMessage,
        attempts = job.Attempts,
        createdAt = job.CreatedAt,
        updatedAt = job.UpdatedAt
    };

    /// <summary>El payload se guardó como JSON, pero si alguna fila viejo no lo es se devuelve como texto en vez de fallar.</summary>
    private static JsonNode? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return JsonValue.Create(json);
        }
    }
}
