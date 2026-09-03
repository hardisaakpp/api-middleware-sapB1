using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.InventoryTransferRequests;

public sealed class GetInventoryTransferRequestUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetInventoryTransferRequestUseCase> _logger;

    public GetInventoryTransferRequestUseCase(ISapGateway gateway, ILogger<GetInventoryTransferRequestUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<InventoryTransferRequestResponse?> ExecuteAsync(GetInventoryTransferRequestRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting InventoryTransferRequest DocEntry={DocEntry}", request.DocEntry);

        return await _gateway.GetInventoryTransferRequestAsync(request.DocEntry, cancellationToken);
    }
}
