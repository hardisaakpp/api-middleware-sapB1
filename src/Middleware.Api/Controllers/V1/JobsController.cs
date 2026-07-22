using System.Text.Json.Nodes;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Abstractions;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/jobs")]
public sealed class JobsController : ControllerBase
{
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

        return Ok(new
        {
            jobId = job.Id,
            endpoint = job.Endpoint,
            status = job.Status,
            httpStatusCode = job.HttpStatusCode,
            result = job.ResultPayloadJson is null ? null : JsonNode.Parse(job.ResultPayloadJson),
            error = job.ErrorMessage,
            attempts = job.Attempts,
            createdAt = job.CreatedAt,
            updatedAt = job.UpdatedAt
        });
    }
}
