using System.Net;
using System.Text.Json;
using Middleware.Application.Abstractions;
using Middleware.Application.UseCases.Items;

namespace Middleware.Application.Jobs.Handlers;

public sealed record GetItemStockJobRequest(string ItemCode, string WarehouseCode);

public sealed class GetItemStockJobHandler : IJobHandler
{
    private readonly GetItemStockUseCase _useCase;

    public string RequestType => "GetItemStock";

    public GetItemStockJobHandler(GetItemStockUseCase useCase)
    {
        _useCase = useCase;
    }

    public async Task<JobAttemptResult> ExecuteAsync(string requestPayloadJson, CancellationToken ct = default)
    {
        var request = JsonSerializer.Deserialize<GetItemStockJobRequest>(requestPayloadJson, JobJsonOptions.Default)
            ?? throw new InvalidOperationException($"Invalid queued payload for {RequestType}.");

        var response = await _useCase.ExecuteAsync(request.ItemCode, request.WarehouseCode, ct);
        return response is null
            ? new JobAttemptResult((int)HttpStatusCode.NotFound, "null")
            : new JobAttemptResult((int)HttpStatusCode.OK, JsonSerializer.Serialize(response, JobJsonOptions.Default));
    }
}
