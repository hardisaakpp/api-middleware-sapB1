using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Items;

public sealed class GetItemStockAllWarehousesUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetItemStockAllWarehousesUseCase> _logger;

    public GetItemStockAllWarehousesUseCase(ISapGateway gateway, ILogger<GetItemStockAllWarehousesUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ItemStockResponse>?> ExecuteAsync(string itemCode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting stock in all warehouses for ItemCode={ItemCode}", itemCode);
        return await _gateway.GetItemStockAllWarehousesAsync(itemCode, cancellationToken);
    }
}
