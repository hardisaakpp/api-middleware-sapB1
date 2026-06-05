using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.StockTransfer;

public sealed class CreateStockTransferUseCase : IUseCase<CreateStockTransferRequest, StockTransferResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<CreateStockTransferUseCase> _logger;

    public CreateStockTransferUseCase(ISapGateway gateway, ILogger<CreateStockTransferUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<StockTransferResponse> ExecuteAsync(CreateStockTransferRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating StockTransfer from {FromWarehouse} to {ToWarehouse} with {LineCount} lines",
            request.FromWarehouse, request.ToWarehouse, request.Lines.Count);

        var result = await _gateway.CreateStockTransferAsync(request, cancellationToken);

        _logger.LogInformation("StockTransfer created: DocEntry={DocEntry}, DocNum={DocNum}", result.DocEntry, result.DocNum);

        return new StockTransferResponse(result.DocEntry, result.DocNum);
    }
}
