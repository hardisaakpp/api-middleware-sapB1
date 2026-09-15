using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

/// <summary>GET individual de una InventoryTransferRequest (SOT) por DocEntry — mismo patrón que GetPurchaseOrderRequest.</summary>
public sealed record GetInventoryTransferRequestRequest(int DocEntry);

public sealed class GetInventoryTransferRequestRequestValidator : AbstractValidator<GetInventoryTransferRequestRequest>
{
    public GetInventoryTransferRequestRequestValidator()
    {
        RuleFor(x => x.DocEntry).GreaterThan(0);
    }
}
