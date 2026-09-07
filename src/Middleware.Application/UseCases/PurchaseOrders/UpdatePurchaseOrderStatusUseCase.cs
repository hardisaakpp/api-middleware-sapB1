using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.PurchaseOrders;

public sealed class UpdatePurchaseOrderStatusUseCase
    : IUseCase<UpdatePurchaseOrderStatusRequest, UpdatePurchaseOrderStatusResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<UpdatePurchaseOrderStatusUseCase> _logger;

    public UpdatePurchaseOrderStatusUseCase(ISapGateway gateway, ILogger<UpdatePurchaseOrderStatusUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<UpdatePurchaseOrderStatusResponse> ExecuteAsync(
        UpdatePurchaseOrderStatusRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating PurchaseOrder DocEntry={DocEntry} to U_STATUS={Status}", request.DocEntry, request.U_STATUS);

        await _gateway.UpdatePurchaseOrderStatusAsync(request.DocEntry, request.U_STATUS, cancellationToken);

        _logger.LogInformation("PurchaseOrder status updated successfully: DocEntry={DocEntry}", request.DocEntry);

        return new UpdatePurchaseOrderStatusResponse(true, request.DocEntry, request.U_STATUS);
    }
}
