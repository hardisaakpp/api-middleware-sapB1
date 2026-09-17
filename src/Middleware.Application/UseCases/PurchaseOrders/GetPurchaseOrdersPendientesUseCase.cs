using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.PurchaseOrders;

public sealed class GetPurchaseOrdersPendientesUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetPurchaseOrdersPendientesUseCase> _logger;

    public GetPurchaseOrdersPendientesUseCase(ISapGateway gateway, ILogger<GetPurchaseOrdersPendientesUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PurchaseOrderResponse>> ExecuteAsync(GetPurchaseOrdersPendientesRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Getting PurchaseOrders pendientes Desde={Desde} Hasta={Hasta} WarehouseCodes={WarehouseCodes}",
            request.Desde, request.Hasta, string.Join(",", request.WarehouseCodes));

        return await _gateway.GetPurchaseOrdersPendientesAsync(request, cancellationToken);
    }
}
