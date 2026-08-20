using System.Net;
using System.Text.Json;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.WebPos;

namespace Middleware.Application.Jobs.Handlers;

public sealed class NotifyWebPosStockTransferJobHandler : IJobHandler
{
    private readonly NotifyWebPosStockTransferUseCase _useCase;

    public string RequestType => "NotifyWebPosStockTransfer";

    public NotifyWebPosStockTransferJobHandler(NotifyWebPosStockTransferUseCase useCase)
    {
        _useCase = useCase;
    }

    public async Task<JobAttemptResult> ExecuteAsync(string requestPayloadJson, CancellationToken ct = default)
    {
        var request = JsonSerializer.Deserialize<NotifyWebPosStockTransferRequest>(requestPayloadJson, JobJsonOptions.Default)
            ?? throw new InvalidOperationException($"Invalid queued payload for {RequestType}.");

        var response = await _useCase.ExecuteAsync(request, ct);
        return new JobAttemptResult((int)HttpStatusCode.OK, JsonSerializer.Serialize(response, JobJsonOptions.Default));
    }
}
