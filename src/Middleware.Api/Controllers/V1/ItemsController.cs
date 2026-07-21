using System.Net;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.Jobs;
using Middleware.Application.Jobs.Handlers;
using Middleware.Application.UseCases.Items;

namespace Middleware.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/items")]
public sealed class ItemsController : ControllerBase // NOSONAR
{
    // TODO: el commit 50323be (feature/middleware-odc) agregaba aqui POST /items/batch
    // (GetItemsBatchUseCase + IValidator<GetItemsBatchRequest>) sobre el patron viejo de
    // use cases directos. Se descarto en el cherry-pick porque este controller ya usa el
    // patron job-queue (IJobExecutionCoordinator). Pendiente decidir si se reintegra como
    // JobHandler.
    private readonly IJobExecutionCoordinator _jobCoordinator;

    public ItemsController(IJobExecutionCoordinator jobCoordinator)
    {
        _jobCoordinator = jobCoordinator;
    }

    [HttpGet("{itemCode}")]
    [ProducesResponseType(typeof(ItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string itemCode, CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(new GetItemJobRequest(itemCode));

        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetItem",
            $"GET /api/v1/items/{itemCode}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetItemUseCase>().ExecuteAsync(itemCode, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<ItemResponse?> c => c.Response is null ? NotFound() : Ok(c.Response),
            JobQueued<ItemResponse?> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet("{itemCode}/stock/{warehouseCode}")]
    [ProducesResponseType(typeof(ItemStockResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStock(
        string itemCode,
        string warehouseCode,
        CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(new GetItemStockJobRequest(itemCode, warehouseCode));

        var outcome = await _jobCoordinator.ExecuteAsync(
            "GetItemStock",
            $"GET /api/v1/items/{itemCode}/stock/{warehouseCode}",
            payloadJson,
            (sp, ct) => sp.GetRequiredService<GetItemStockUseCase>().ExecuteAsync(itemCode, warehouseCode, ct),
            (int)HttpStatusCode.OK,
            cancellationToken);

        return outcome switch
        {
            JobCompleted<ItemStockResponse?> c => c.Response is null ? NotFound() : Ok(c.Response),
            JobQueued<ItemStockResponse?> q => Accepted(new { jobId = q.JobId, status = JobStatuses.Encolado }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
