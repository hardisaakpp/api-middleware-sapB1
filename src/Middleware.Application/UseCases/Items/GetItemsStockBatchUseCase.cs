using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Items;

public sealed class GetItemsStockBatchUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetItemsStockBatchUseCase> _logger;

    public GetItemsStockBatchUseCase(ISapGateway gateway, ILogger<GetItemsStockBatchUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;

    }
    public async Task<ItemsStockBatchResponse> ExecuteAsync(
        GetItemsStockBatchRequest request, CancellationToken cancellationToken = default
    )
    {
        var codes = request.ItemCodes
        .Select(c => c.Trim())
        .Where(c => c.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        if (codes.Count == 0)
        {
            return new ItemsStockBatchResponse([], []);
        }

        _logger.LogInformation(
           "Items stock batch: WarehouseCode={WarehouseCode}, UniqueCodes={CodeCount}",
           request.WarehouseCode, codes.Count);

        var (items, notFound) = await _gateway.GetItemsStockBatchAsync(request.WarehouseCode, codes, cancellationToken);

        return new ItemsStockBatchResponse(items, notFound);

    }

}