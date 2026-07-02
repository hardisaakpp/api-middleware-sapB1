using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record GetPurchaseOrderRequest(int DocEntry);

public sealed class GetPurchaseOrderRequestValidator : AbstractValidator<GetPurchaseOrderRequest>
{
    public GetPurchaseOrderRequestValidator()
    {
        RuleFor(x => x.DocEntry).GreaterThan(0);
    }
}
