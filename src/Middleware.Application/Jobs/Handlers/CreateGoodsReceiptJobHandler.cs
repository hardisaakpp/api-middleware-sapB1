using System.Net;
using System.Text.Json;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.GoodsReceipt;

namespace Middleware.Application.Jobs.Handlers;

public sealed class CreateGoodsReceiptJobHandler : IJobHandler
{
    private readonly CreateGoodsReceiptUseCase _useCase;

    public string RequestType => "CreateGoodsReceipt";

    public CreateGoodsReceiptJobHandler(CreateGoodsReceiptUseCase useCase)
    {
        _useCase = useCase;
    }

    public async Task<JobAttemptResult> ExecuteAsync(string requestPayloadJson, CancellationToken ct = default)
    {
        var request = JsonSerializer.Deserialize<CreateGoodsReceiptRequest>(requestPayloadJson, JobJsonOptions.Default)
            ?? throw new InvalidOperationException($"Invalid queued payload for {RequestType}.");

        var response = await _useCase.ExecuteAsync(request, ct);
        return new JobAttemptResult((int)HttpStatusCode.Created, JsonSerializer.Serialize(response, JobJsonOptions.Default));
    }
}
