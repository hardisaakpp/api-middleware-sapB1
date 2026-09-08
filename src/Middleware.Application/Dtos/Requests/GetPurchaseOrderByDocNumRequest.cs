using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record GetPurchaseOrderByDocNumRequest(int DocNum);

public sealed class GetPurchaseOrderByDocNumRequestValidator : AbstractValidator<GetPurchaseOrderByDocNumRequest>
{
    public GetPurchaseOrderByDocNumRequestValidator()
    {
        RuleFor(x => x.DocNum).GreaterThan(0);
    }
}
