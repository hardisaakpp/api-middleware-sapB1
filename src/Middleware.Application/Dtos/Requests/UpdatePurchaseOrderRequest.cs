using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record UpdatePurchaseOrderRequest(
    int DocEntry,
    IReadOnlyList<UpdatePurchaseOrderLineDto> Lines
);

public sealed record UpdatePurchaseOrderLineDto(
    int LineNum,
    decimal Quantity,
    string? WarehouseCode = null
);

public sealed class UpdatePurchaseOrderRequestValidator : AbstractValidator<UpdatePurchaseOrderRequest>
{
    public UpdatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.DocEntry).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.LineNum).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });
    }
}
