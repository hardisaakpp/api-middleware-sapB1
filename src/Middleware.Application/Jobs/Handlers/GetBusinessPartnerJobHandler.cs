using System.Net;
using System.Text.Json;
using Middleware.Application.Abstractions;
using Middleware.Application.UseCases.BusinessPartners;

namespace Middleware.Application.Jobs.Handlers;

public sealed record GetBusinessPartnerJobRequest(string CardCode);

public sealed class GetBusinessPartnerJobHandler : IJobHandler
{
    private readonly GetBusinessPartnerUseCase _useCase;

    public string RequestType => "GetBusinessPartner";

    public GetBusinessPartnerJobHandler(GetBusinessPartnerUseCase useCase)
    {
        _useCase = useCase;
    }

    public async Task<JobAttemptResult> ExecuteAsync(string requestPayloadJson, CancellationToken ct = default)
    {
        var request = JsonSerializer.Deserialize<GetBusinessPartnerJobRequest>(requestPayloadJson, JobJsonOptions.Default)
            ?? throw new InvalidOperationException($"Invalid queued payload for {RequestType}.");

        var response = await _useCase.ExecuteAsync(request.CardCode, ct);
        return response is null
            ? new JobAttemptResult((int)HttpStatusCode.NotFound, "null")
            : new JobAttemptResult((int)HttpStatusCode.OK, JsonSerializer.Serialize(response, JobJsonOptions.Default));
    }
}
