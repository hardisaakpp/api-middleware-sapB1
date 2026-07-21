using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.InventoryTransferRequests;

public sealed class UpdateInventoryTransferRequestStatusUseCase
    : IUseCase<UpdateInventoryTransferRequestStatusRequest, UpdateInventoryTransferRequestStatusResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<UpdateInventoryTransferRequestStatusUseCase> _logger;

    public UpdateInventoryTransferRequestStatusUseCase(ISapGateway gateway, ILogger<UpdateInventoryTransferRequestStatusUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<UpdateInventoryTransferRequestStatusResponse> ExecuteAsync(
        UpdateInventoryTransferRequestStatusRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating InventoryTransferRequest DocEntry={DocEntry} to U_STATUS={Status}", request.DocEntry, request.U_STATUS);

        await _gateway.UpdateInventoryTransferRequestStatusAsync(request.DocEntry, request.U_STATUS, cancellationToken);

        _logger.LogInformation("InventoryTransferRequest status updated successfully: DocEntry={DocEntry}", request.DocEntry);

        return new UpdateInventoryTransferRequestStatusResponse(true, request.DocEntry, request.U_STATUS);
    }
}
