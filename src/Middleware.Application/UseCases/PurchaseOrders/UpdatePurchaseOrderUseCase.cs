using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.PurchaseOrders;

public sealed class UpdatePurchaseOrderUseCase : IUseCase<UpdatePurchaseOrderRequest, UpdatePurchaseOrderResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<UpdatePurchaseOrderUseCase> _logger;

    public UpdatePurchaseOrderUseCase(ISapGateway gateway, ILogger<UpdatePurchaseOrderUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<UpdatePurchaseOrderResponse> ExecuteAsync(UpdatePurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating PurchaseOrder DocEntry={DocEntry} with {LineCount} lines", request.DocEntry, request.Lines.Count);

        await _gateway.UpdatePurchaseOrderAsync(request.DocEntry, request, cancellationToken);

        _logger.LogInformation("PurchaseOrder updated successfully: DocEntry={DocEntry}", request.DocEntry);

        return new UpdatePurchaseOrderResponse(true, request.DocEntry);
    }
}
