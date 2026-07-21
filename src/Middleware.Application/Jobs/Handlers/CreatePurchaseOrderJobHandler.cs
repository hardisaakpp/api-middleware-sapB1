using System.Net;
using System.Text.Json;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.PurchaseOrders;

namespace Middleware.Application.Jobs.Handlers;

public sealed class CreatePurchaseOrderJobHandler : IJobHandler
{
    private readonly CreatePurchaseOrderUseCase _useCase;

    public string RequestType => "CreatePurchaseOrder";

    public CreatePurchaseOrderJobHandler(CreatePurchaseOrderUseCase useCase)
    {
        _useCase = useCase;
    }

    public async Task<JobAttemptResult> ExecuteAsync(string requestPayloadJson, CancellationToken ct = default)
    {
        var request = JsonSerializer.Deserialize<CreatePurchaseOrderRequest>(requestPayloadJson, JobJsonOptions.Default)
            ?? throw new InvalidOperationException($"Invalid queued payload for {RequestType}.");

        var response = await _useCase.ExecuteAsync(request, ct);
        return new JobAttemptResult((int)HttpStatusCode.Created, JsonSerializer.Serialize(response, JobJsonOptions.Default));
    }
}
