using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.StockTransfer;

public sealed class CreateStockTransferDirectUseCase : IUseCase<CreateStockTransferDirectRequest, StockTransferResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<CreateStockTransferDirectUseCase> _logger;

    public CreateStockTransferDirectUseCase(ISapGateway gateway, ILogger<CreateStockTransferDirectUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<StockTransferResponse> ExecuteAsync(CreateStockTransferDirectRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating direct StockTransfer from {FromWarehouse} to {ToWarehouse} with {LineCount} lines",
            request.FromWarehouse, request.ToWarehouse, request.StockTransferLines.Count);

        var result = await _gateway.CreateStockTransferDirectAsync(request, cancellationToken);

        _logger.LogInformation("Direct StockTransfer created: DocEntry={DocEntry}, DocNum={DocNum}", result.DocEntry, result.DocNum);

        return new StockTransferResponse(result.DocEntry, result.DocNum);
    }
}
