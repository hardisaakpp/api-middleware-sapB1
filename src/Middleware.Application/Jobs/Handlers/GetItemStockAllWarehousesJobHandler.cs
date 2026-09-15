using System.Net;
using System.Text.Json;
using Middleware.Application.Abstractions;
using Middleware.Application.UseCases.Items;

namespace Middleware.Application.Jobs.Handlers;

public sealed record GetItemStockAllWarehousesJobRequest(string ItemCode);

public sealed class GetItemStockAllWarehousesJobHandler : IJobHandler
{
    private readonly GetItemStockAllWarehousesUseCase _useCase;

    public string RequestType => "GetItemStockAllWarehouses";

    public GetItemStockAllWarehousesJobHandler(GetItemStockAllWarehousesUseCase useCase)
    {
        _useCase = useCase;
    }

    public async Task<JobAttemptResult> ExecuteAsync(string requestPayloadJson, CancellationToken ct = default)
    {
        var request = JsonSerializer.Deserialize<GetItemStockAllWarehousesJobRequest>(requestPayloadJson, JobJsonOptions.Default)
            ?? throw new InvalidOperationException($"Invalid queued payload for {RequestType}.");

        var response = await _useCase.ExecuteAsync(request.ItemCode, ct);
        return response is null
            ? new JobAttemptResult((int)HttpStatusCode.NotFound, "null")
            : new JobAttemptResult((int)HttpStatusCode.OK, JsonSerializer.Serialize(response, JobJsonOptions.Default));
    }
}
