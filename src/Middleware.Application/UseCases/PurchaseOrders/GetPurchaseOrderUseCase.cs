using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.PurchaseOrders;

public sealed class GetPurchaseOrderUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetPurchaseOrderUseCase> _logger;

    public GetPurchaseOrderUseCase(ISapGateway gateway, ILogger<GetPurchaseOrderUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<PurchaseOrderResponse?> ExecuteAsync(GetPurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting PurchaseOrder DocEntry={DocEntry}", request.DocEntry);

        return await _gateway.GetPurchaseOrderAsync(request.DocEntry, cancellationToken);
    }
}
