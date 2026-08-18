using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.Sql;

internal sealed class SqlRepository : ISqlRepository
{
    private readonly SqlSettings _settings;
    private readonly ILogger<SqlRepository> _logger;

    public SqlRepository(IOptions<SqlSettings> settings, ILogger<SqlRepository> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<decimal> GetAvailableStockAsync(string itemCode, string warehouseCode, CancellationToken ct = default)
    {
        _logger.LogDebug("GetAvailableStock ItemCode={ItemCode} WarehouseCode={WarehouseCode}", itemCode, warehouseCode);

        await using var connection = new SqlConnection(_settings.ConnectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand("sp_GetAvailableStock", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("@ItemCode", itemCode);
        command.Parameters.AddWithValue("@WarehouseCode", warehouseCode);

        var result = await command.ExecuteScalarAsync(ct);

        return result is null or DBNull ? 0m : Convert.ToDecimal(result);
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetAvailableStockBatchAsync(
    IReadOnlyList<string> itemCodes, string warehouseCode, CancellationToken ct = default)
    {
        var results = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var sync = new object();

        using var gate = new SemaphoreSlim(10);
        var tasks = itemCodes.Select(async itemCode =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var stock = await GetAvailableStockAsync(itemCode, warehouseCode, ct);
                lock (sync)
                {
                    results[itemCode] = stock;
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results;
    }


}
