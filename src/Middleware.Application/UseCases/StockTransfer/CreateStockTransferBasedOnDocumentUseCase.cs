using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.StockTransfer;

public sealed class CreateStockTransferBasedOnDocumentUseCase : IUseCase<CreateStockTransferBasedOnDocumentRequest, StockTransferResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<CreateStockTransferBasedOnDocumentUseCase> _logger;

    public CreateStockTransferBasedOnDocumentUseCase(ISapGateway gateway, ILogger<CreateStockTransferBasedOnDocumentUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<StockTransferResponse> ExecuteAsync(CreateStockTransferBasedOnDocumentRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating StockTransfer based on document from {FromWarehouse} to {ToWarehouse} with {LineCount} lines",
            request.FromWarehouse, request.ToWarehouse, request.StockTransferLines.Count);

        var result = await _gateway.CreateStockTransferBasedOnDocumentAsync(request, cancellationToken);

        _logger.LogInformation("StockTransfer based on document created: DocEntry={DocEntry}, DocNum={DocNum}", result.DocEntry, result.DocNum);

        return new StockTransferResponse(result.DocEntry, result.DocNum);
    }
}
