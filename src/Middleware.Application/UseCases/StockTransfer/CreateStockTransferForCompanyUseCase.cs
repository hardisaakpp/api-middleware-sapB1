using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.StockTransfer;

/// <summary>
/// Igual que CreateStockTransferBasedOnDocumentUseCase pero contra Sap:CompanyDb (compañía
/// real, no el sandbox de WebPOS) — SIN notificación WebPOS, porque esa integración es
/// específica de la compañía de test y notificarla acá sería incorrecto (el movimiento no
/// pasó por esa compañía).
/// </summary>
public sealed class CreateStockTransferForCompanyUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<CreateStockTransferForCompanyUseCase> _logger;

    public CreateStockTransferForCompanyUseCase(ISapGateway gateway, ILogger<CreateStockTransferForCompanyUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<StockTransferResponse> ExecuteAsync(CreateStockTransferBasedOnDocumentRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating StockTransfer (Sap:CompanyDb) from {FromWarehouse} to {ToWarehouse} with {LineCount} lines",
            request.FromWarehouse, request.ToWarehouse, request.StockTransferLines.Count);

        var result = await _gateway.CreateStockTransferForCompanyAsync(request, cancellationToken);

        _logger.LogInformation("StockTransfer created: DocEntry={DocEntry}, DocNum={DocNum}", result.DocEntry, result.DocNum);

        return new StockTransferResponse(result.DocEntry, result.DocNum);
    }
}
