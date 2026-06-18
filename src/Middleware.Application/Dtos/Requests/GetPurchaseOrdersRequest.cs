using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record GetPurchaseOrdersRequest(int Top = 20, int Skip = 0);

public sealed class GetPurchaseOrdersRequestValidator : AbstractValidator<GetPurchaseOrdersRequest>
{
    public GetPurchaseOrdersRequestValidator()
    {
        RuleFor(x => x.Top).InclusiveBetween(1, 100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
    }
}
