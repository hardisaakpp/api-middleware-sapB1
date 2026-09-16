using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.GoodsReceipt;

public sealed class GetGoodsReceiptsByBaseEntryUseCase : IUseCase<GetGoodsReceiptsByBaseEntryRequest, GoodsReceiptsByBaseEntryResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetGoodsReceiptsByBaseEntryUseCase> _logger;

    public GetGoodsReceiptsByBaseEntryUseCase(ISapGateway gateway, ILogger<GetGoodsReceiptsByBaseEntryUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<GoodsReceiptsByBaseEntryResponse> ExecuteAsync(GetGoodsReceiptsByBaseEntryRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching GoodsReceipts with BaseEntry={BaseEntry}", request.BaseEntry);

        var receipts = await _gateway.GetGoodsReceiptsByBaseEntryAsync(request.BaseEntry, request.VendorCode, cancellationToken);

        return new GoodsReceiptsByBaseEntryResponse(receipts);
    }
}
