using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.PurchaseOrders;

public sealed class CreatePurchaseOrderUseCase : IUseCase<CreatePurchaseOrderRequest, CreatePurchaseOrderResponse>
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<CreatePurchaseOrderUseCase> _logger;

    public CreatePurchaseOrderUseCase(ISapGateway gateway, ILogger<CreatePurchaseOrderUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<CreatePurchaseOrderResponse> ExecuteAsync(
        CreatePurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating PurchaseOrder for CardCode={CardCode}, DocDate={DocDate}, Lines={LineCount}",
            request.CardCode, request.DocDate, request.Lines.Count);

        var result = await _gateway.CreatePurchaseOrderAsync(request, cancellationToken);

        _logger.LogInformation(
            "PurchaseOrder created: DocEntry={DocEntry}, DocNum={DocNum}",
            result.DocEntry, result.DocNum);

        return new CreatePurchaseOrderResponse(result.DocEntry, result.DocNum);
    }
}
