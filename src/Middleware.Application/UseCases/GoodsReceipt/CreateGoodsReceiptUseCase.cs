using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.GoodsReceipt;

public sealed class CreateGoodsReceiptUseCase : IUseCase<CreateGoodsReceiptRequest, GoodsReceiptResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<CreateGoodsReceiptUseCase> _logger;

    public CreateGoodsReceiptUseCase(ISapGateway gateway, ILogger<CreateGoodsReceiptUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<GoodsReceiptResponse> ExecuteAsync(CreateGoodsReceiptRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating GoodsReceipt for vendor {VendorCode} with {LineCount} lines",
            request.VendorCode, request.Lines.Count);

        var result = await _gateway.CreateGoodsReceiptAsync(request, cancellationToken);

        _logger.LogInformation("GoodsReceipt created: DocEntry={DocEntry}, DocNum={DocNum}", result.DocEntry, result.DocNum);

        return new GoodsReceiptResponse(result.DocEntry, result.DocNum);
    }
}
