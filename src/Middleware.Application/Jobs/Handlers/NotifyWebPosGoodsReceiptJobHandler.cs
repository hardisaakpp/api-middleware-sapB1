using System.Net;
using System.Text.Json;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.WebPos;

namespace Middleware.Application.Jobs.Handlers;

public sealed class NotifyWebPosGoodsReceiptJobHandler : IJobHandler
{
    private readonly NotifyWebPosGoodsReceiptUseCase _useCase;

    public string RequestType => "NotifyWebPosGoodsReceipt";

    public NotifyWebPosGoodsReceiptJobHandler(NotifyWebPosGoodsReceiptUseCase useCase)
    {
        _useCase = useCase;
    }

    public async Task<JobAttemptResult> ExecuteAsync(string requestPayloadJson, CancellationToken ct = default)
    {
        var request = JsonSerializer.Deserialize<NotifyWebPosGoodsReceiptRequest>(requestPayloadJson, JobJsonOptions.Default)
            ?? throw new InvalidOperationException($"Invalid queued payload for {RequestType}.");

        var response = await _useCase.ExecuteAsync(request, ct);
        return new JobAttemptResult((int)HttpStatusCode.OK, JsonSerializer.Serialize(response, JobJsonOptions.Default));
    }
}
