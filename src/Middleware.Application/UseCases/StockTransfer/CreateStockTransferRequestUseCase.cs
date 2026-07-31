using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.StockTransfer;

public sealed class CreateStockTransferRequestUseCase : IUseCase<CreateStockTransferRequest, StockTransferResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<CreateStockTransferRequestUseCase> _logger;

    public CreateStockTransferRequestUseCase(ISapGateway gateway, ILogger<CreateStockTransferRequestUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<StockTransferResponse> ExecuteAsync(CreateStockTransferRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating InventoryTransferRequest from {FromWarehouse} to {ToWarehouse} with {LineCount} lines",
            request.FromWarehouse, request.ToWarehouse, request.StockTransferLines.Count);

        var result = await _gateway.CreateStockTransferRequestAsync(request, cancellationToken);

        _logger.LogInformation("StockTransferRequest created: DocEntry={DocEntry}, DocNum={DocNum}", result.DocEntry, result.DocNum);

        return new StockTransferResponse(result.DocEntry, result.DocNum);
    }
}
