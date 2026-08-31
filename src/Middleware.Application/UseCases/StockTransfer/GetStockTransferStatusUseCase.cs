using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.StockTransfer;

public sealed class GetStockTransferStatusUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetStockTransferStatusUseCase> _logger;

    public GetStockTransferStatusUseCase(ISapGateway gateway, ILogger<GetStockTransferStatusUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<StockTransferStatusResponse?> ExecuteAsync(int docEntry, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting StockTransfer status for DocEntry={DocEntry}", docEntry);
        return await _gateway.GetStockTransferStatusAsync(docEntry, cancellationToken);
    }
}
