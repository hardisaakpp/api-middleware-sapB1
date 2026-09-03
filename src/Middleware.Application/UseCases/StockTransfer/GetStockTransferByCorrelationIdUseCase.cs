
using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.StockTransfer;

public sealed class GetStockTransferByCorrelationIdUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetStockTransferByCorrelationIdUseCase> _logger;

    public GetStockTransferByCorrelationIdUseCase(ISapGateway gateway, ILogger<GetStockTransferByCorrelationIdUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<StockTransferResponse?> ExecuteAsync(string uSapId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Looking up StockTransfer by U_sap_id={USapId}", uSapId);

        var result = await _gateway.GetStockTransferByCorrelationIdAsync(uSapId, cancellationToken);

        return result is null ? null : new StockTransferResponse(result.DocEntry, result.DocNum);
    }
}