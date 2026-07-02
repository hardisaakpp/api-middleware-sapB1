using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Items;

public sealed class GetItemStockUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetItemStockUseCase> _logger;

    public GetItemStockUseCase(ISapGateway gateway, ILogger<GetItemStockUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ItemStockResponse?> ExecuteAsync(string itemCode, string warehouseCode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting stock for ItemCode={ItemCode} WarehouseCode={WarehouseCode}", itemCode, warehouseCode);
        return await _gateway.GetItemStockAsync(itemCode, warehouseCode, cancellationToken);
    }
}
