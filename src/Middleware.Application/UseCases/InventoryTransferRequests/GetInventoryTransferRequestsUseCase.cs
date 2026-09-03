using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.InventoryTransferRequests;

public sealed class GetInventoryTransferRequestsUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetInventoryTransferRequestsUseCase> _logger;

    public GetInventoryTransferRequestsUseCase(ISapGateway gateway, ILogger<GetInventoryTransferRequestsUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<InventoryTransferRequestsRawListResponse> ExecuteAsync(GetInventoryTransferRequestsRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting InventoryTransferRequests Top={Top} Skip={Skip}", request.Top, request.Skip);

        return await _gateway.GetInventoryTransferRequestsAsync(request, cancellationToken);
    }
}
