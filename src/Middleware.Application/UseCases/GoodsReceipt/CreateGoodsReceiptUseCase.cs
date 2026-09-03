using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.GoodsReceipt;

public sealed class CreateGoodsReceiptUseCase : IUseCase<CreateGoodsReceiptRequest, GoodsReceiptResponse>
{
    private readonly ISapGateway _gateway;
    private readonly IWebPosGoodsReceiptNotifier _webPosNotifier;
    private readonly ILogger<CreateGoodsReceiptUseCase> _logger;

    public CreateGoodsReceiptUseCase(
        ISapGateway gateway, IWebPosGoodsReceiptNotifier webPosNotifier, ILogger<CreateGoodsReceiptUseCase> logger)
    {
        _gateway = gateway;
        _webPosNotifier = webPosNotifier;
        _logger = logger;
    }

    public async Task<GoodsReceiptResponse> ExecuteAsync(CreateGoodsReceiptRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating GoodsReceipt for vendor {VendorCode} with {LineCount} lines",
            request.VendorCode, request.Lines.Count);

        var result = await _gateway.CreateGoodsReceiptAsync(request, cancellationToken);

        _logger.LogInformation("GoodsReceipt created: DocEntry={DocEntry}, DocNum={DocNum}", result.DocEntry, result.DocNum);

        // SAP receipt is a done deal at this point — a slow/failing WEBPOS never re-triggers this,
        // same contract as the stock-transfer flow.
        var webPosSync = await _webPosNotifier.NotifyAsync(
            result.DocEntry, result.DocNum,
            request.Lines.Select(l => new NotifyWebPosGoodsReceiptLine(l.ItemCode, l.Quantity, l.WarehouseCode)).ToList(),
            cancellationToken);

        return new GoodsReceiptResponse(result.DocEntry, result.DocNum, webPosSync);
    }
}
