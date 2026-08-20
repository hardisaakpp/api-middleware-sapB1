using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.StockTransfer;

public sealed class CreateStockTransferDirectUseCase : IUseCase<CreateStockTransferDirectRequest, StockTransferResponse>
{
    private readonly ISapGateway _gateway;
    private readonly IWebPosStockTransferNotifier _webPosNotifier;
    private readonly ILogger<CreateStockTransferDirectUseCase> _logger;

    public CreateStockTransferDirectUseCase(
        ISapGateway gateway, IWebPosStockTransferNotifier webPosNotifier, ILogger<CreateStockTransferDirectUseCase> logger)
    {
        _gateway = gateway;
        _webPosNotifier = webPosNotifier;
        _logger = logger;
    }

    public async Task<StockTransferResponse> ExecuteAsync(CreateStockTransferDirectRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating direct StockTransfer from {FromWarehouse} to {ToWarehouse} with {LineCount} lines",
            request.FromWarehouse, request.ToWarehouse, request.StockTransferLines.Count);

        var result = await _gateway.CreateStockTransferDirectAsync(request, cancellationToken);

        _logger.LogInformation("Direct StockTransfer created: DocEntry={DocEntry}, DocNum={DocNum}", result.DocEntry, result.DocNum);

        // SAP transfer is a done deal at this point — a slow/failing WEBPOS never re-triggers this.
        var webPosSync = await _webPosNotifier.NotifyAsync(
            result.DocEntry, result.DocNum, request.FromWarehouse, request.ToWarehouse,
            request.StockTransferLines.Select(l => new NotifyWebPosStockTransferLine(l.ItemCode, l.Quantity)).ToList(),
            cancellationToken);

        return new StockTransferResponse(result.DocEntry, result.DocNum, webPosSync);
    }
}
