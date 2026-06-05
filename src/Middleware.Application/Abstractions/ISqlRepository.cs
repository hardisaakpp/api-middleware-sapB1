namespace Middleware.Application.Abstractions;

public interface ISqlRepository
{
    Task<decimal> GetAvailableStockAsync(string itemCode, string warehouseCode, CancellationToken ct = default);
}
