namespace Middleware.Application.Abstractions;

public interface ISqlRepository
{
    public Task<decimal> GetAvailableStockAsync(string itemCode, string warehouseCode, CancellationToken ct = default);
}
