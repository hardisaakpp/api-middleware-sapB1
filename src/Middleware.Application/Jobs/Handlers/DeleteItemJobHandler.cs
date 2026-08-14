using System.Net;
using System.Text.Json;
using Middleware.Application.Abstractions;
using Middleware.Application.UseCases.Items;

namespace Middleware.Application.Jobs.Handlers;

public sealed record DeleteItemJobRequest(string ItemCode);

public sealed class DeleteItemJobHandler : IJobHandler
{
    private readonly DeleteItemUseCase _useCase;

    public string RequestType => "DeleteItem";

    public DeleteItemJobHandler(DeleteItemUseCase useCase)
    {
        _useCase = useCase;
    }

    public async Task<JobAttemptResult> ExecuteAsync(string requestPayloadJson, CancellationToken ct = default)
    {
        var request = JsonSerializer.Deserialize<DeleteItemJobRequest>(requestPayloadJson, JobJsonOptions.Default)
            ?? throw new InvalidOperationException($"Invalid queued payload for {RequestType}.");

        var response = await _useCase.ExecuteAsync(request.ItemCode, ct);
        return new JobAttemptResult((int)HttpStatusCode.NoContent, JsonSerializer.Serialize(response, JobJsonOptions.Default));
    }
}
