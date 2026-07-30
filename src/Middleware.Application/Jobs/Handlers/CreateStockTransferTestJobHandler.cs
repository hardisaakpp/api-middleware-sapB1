using System.Net;
using System.Text.Json;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.StockTransfer;

namespace Middleware.Application.Jobs.Handlers;

public sealed class CreateStockTransferTestJobHandler : IJobHandler
{
    private readonly CreateStockTransferTestUseCase _useCase;

    public string RequestType => "CreateStockTransferTest";

    public CreateStockTransferTestJobHandler(CreateStockTransferTestUseCase useCase)
    {
        _useCase = useCase;
    }

    public async Task<JobAttemptResult> ExecuteAsync(string requestPayloadJson, CancellationToken ct = default)
    {
        var request = JsonSerializer.Deserialize<CreateStockTransferTestRequest>(requestPayloadJson, JobJsonOptions.Default)
            ?? throw new InvalidOperationException($"Invalid queued payload for {RequestType}.");

        var response = await _useCase.ExecuteAsync(request, ct);
        return new JobAttemptResult((int)HttpStatusCode.Created, JsonSerializer.Serialize(response, JobJsonOptions.Default));
    }
}
