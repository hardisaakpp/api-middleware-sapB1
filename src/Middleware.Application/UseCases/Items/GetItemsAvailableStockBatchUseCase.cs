using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.Items;

public sealed class GetItemsAvailableStockBatchUseCase
{
    private readonly ISqlRepository _sqlRepository;
    private readonly ILogger<GetItemsAvailableStockBatchUseCase> _logger;

    public GetItemsAvailableStockBatchUseCase(ISqlRepository sqlRepository, ILogger<GetItemsAvailableStockBatchUseCase> logger)
    {
        _sqlRepository = sqlRepository;
        _logger = logger;
    }

    public async Task<ItemsAvailableStockBatchResponse> ExecuteAsync(
        GetItemsStockBatchRequest request, CancellationToken cancellationToken = default)
    {
        var codes = request.ItemCodes
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codes.Count == 0)
        {
            return new ItemsAvailableStockBatchResponse([]);
        }

        _logger.LogInformation(
            "Items available-stock batch (SQL): WarehouseCode={WarehouseCode}, UniqueCodes={CodeCount}",
            request.WarehouseCode, codes.Count);

        var stockByCode = await _sqlRepository.GetAvailableStockBatchAsync(codes, request.WarehouseCode, cancellationToken);

        var items = codes
            .Select(code => new ItemAvailableStockResponse(code, request.WarehouseCode, stockByCode.GetValueOrDefault(code, 0m)))
            .ToList();

        return new ItemsAvailableStockBatchResponse(items);
    }
}
