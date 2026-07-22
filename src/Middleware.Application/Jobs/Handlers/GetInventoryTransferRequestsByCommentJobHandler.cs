using System.Net;
using System.Text.Json;
using Middleware.Application.Abstractions;
using Middleware.Application.UseCases.InventoryTransferRequests;

namespace Middleware.Application.Jobs.Handlers;

public sealed record GetInventoryTransferRequestsByCommentJobRequest(string CommentsContain);

public sealed class GetInventoryTransferRequestsByCommentJobHandler : IJobHandler
{
    private readonly GetInventoryTransferRequestsByCommentUseCase _useCase;

    public string RequestType => "GetInventoryTransferRequestsByComment";

    public GetInventoryTransferRequestsByCommentJobHandler(GetInventoryTransferRequestsByCommentUseCase useCase)
    {
        _useCase = useCase;
    }

    public async Task<JobAttemptResult> ExecuteAsync(string requestPayloadJson, CancellationToken ct = default)
    {
        var request = JsonSerializer.Deserialize<GetInventoryTransferRequestsByCommentJobRequest>(requestPayloadJson, JobJsonOptions.Default)
            ?? throw new InvalidOperationException($"Invalid queued payload for {RequestType}.");

        var response = await _useCase.ExecuteAsync(request.CommentsContain, ct);
        return new JobAttemptResult((int)HttpStatusCode.OK, JsonSerializer.Serialize(response, JobJsonOptions.Default));
    }
}
