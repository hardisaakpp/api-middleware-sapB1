using System.Net;
using System.Text.Json;
using Middleware.Application.Abstractions;
using Middleware.Application.UseCases.Items;

namespace Middleware.Application.Jobs.Handlers;

public sealed record GetItemJobRequest(string ItemCode);

public sealed class GetItemJobHandler : IJobHandler
{
    private readonly GetItemUseCase _useCase;

    public string RequestType => "GetItem";

    public GetItemJobHandler(GetItemUseCase useCase)
    {
        _useCase = useCase;
    }

    public async Task<JobAttemptResult> ExecuteAsync(string requestPayloadJson, CancellationToken ct = default)
    {
        var request = JsonSerializer.Deserialize<GetItemJobRequest>(requestPayloadJson, JobJsonOptions.Default)
            ?? throw new InvalidOperationException($"Invalid queued payload for {RequestType}.");

        var response = await _useCase.ExecuteAsync(request.ItemCode, ct);
        return response is null
            ? new JobAttemptResult((int)HttpStatusCode.NotFound, "null")
            : new JobAttemptResult((int)HttpStatusCode.OK, JsonSerializer.Serialize(response, JobJsonOptions.Default));
    }
}
