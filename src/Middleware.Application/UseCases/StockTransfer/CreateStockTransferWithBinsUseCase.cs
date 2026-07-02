using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.StockTransfer;

public sealed class CreateStockTransferWithBinsUseCase : IUseCase<CreateStockTransferWithBinsRequest, StockTransferResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<CreateStockTransferWithBinsUseCase> _logger;

    public CreateStockTransferWithBinsUseCase(ISapGateway gateway, ILogger<CreateStockTransferWithBinsUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<StockTransferResponse> ExecuteAsync(CreateStockTransferWithBinsRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating StockTransfer with bins from {FromWarehouse} to {ToWarehouse} with {LineCount} lines",
            request.FromWarehouse, request.ToWarehouse, request.Lines.Count);

        var result = await _gateway.CreateStockTransferWithBinsAsync(request, cancellationToken);

        _logger.LogInformation("StockTransferWithBins created: DocEntry={DocEntry}, DocNum={DocNum}", result.DocEntry, result.DocNum);

        return new StockTransferResponse(result.DocEntry, result.DocNum);
    }
}
