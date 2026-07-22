using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.PurchaseOrders;

public sealed class GetPurchaseOrdersUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetPurchaseOrdersUseCase> _logger;

    public GetPurchaseOrdersUseCase(ISapGateway gateway, ILogger<GetPurchaseOrdersUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<PurchaseOrdersListResponse> ExecuteAsync(GetPurchaseOrdersRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting PurchaseOrders Top={Top} Skip={Skip}", request.Top, request.Skip);

        return await _gateway.GetPurchaseOrdersAsync(request, cancellationToken);
    }
}
