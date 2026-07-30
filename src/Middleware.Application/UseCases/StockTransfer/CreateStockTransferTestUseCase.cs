using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.StockTransfer;

public sealed class CreateStockTransferTestUseCase : IUseCase<CreateStockTransferRequest, StockTransferResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<CreateStockTransferTestUseCase> _logger;

    public CreateStockTransferTestUseCase(ISapGateway gateway, ILogger<CreateStockTransferTestUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<StockTransferResponse> ExecuteAsync(CreateStockTransferRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating StockTransfer (test CompanyDB) from {FromWarehouse} to {ToWarehouse} with {LineCount} lines",
            request.FromWarehouse, request.ToWarehouse, request.Lines.Count);

        var result = await _gateway.CreateStockTransferTestAsync(request, cancellationToken);

        _logger.LogInformation("StockTransfer (test CompanyDB) created: DocEntry={DocEntry}, DocNum={DocNum}", result.DocEntry, result.DocNum);

        return new StockTransferResponse(result.DocEntry, result.DocNum);
    }
}
