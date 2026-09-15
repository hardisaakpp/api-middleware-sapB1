using System.Net;
using System.Text.Json;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.InventoryTransferRequests;

namespace Middleware.Application.Jobs.Handlers;

public sealed class GetInventoryTransferRequestJobHandler : IJobHandler
{
    private readonly GetInventoryTransferRequestUseCase _useCase;

    public string RequestType => "GetInventoryTransferRequest";

    public GetInventoryTransferRequestJobHandler(GetInventoryTransferRequestUseCase useCase)
    {
        _useCase = useCase;
    }

    public async Task<JobAttemptResult> ExecuteAsync(string requestPayloadJson, CancellationToken ct = default)
    {
        var request = JsonSerializer.Deserialize<GetInventoryTransferRequestRequest>(requestPayloadJson, JobJsonOptions.Default)
            ?? throw new InvalidOperationException($"Invalid queued payload for {RequestType}.");

        var response = await _useCase.ExecuteAsync(request, ct);
        return response is null
            ? new JobAttemptResult((int)HttpStatusCode.NotFound, "null")
            : new JobAttemptResult((int)HttpStatusCode.OK, JsonSerializer.Serialize(response, JobJsonOptions.Default));
    }
}
