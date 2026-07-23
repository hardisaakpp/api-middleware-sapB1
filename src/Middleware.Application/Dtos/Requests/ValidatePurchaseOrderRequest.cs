using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record ValidatePurchaseOrderRequest(
    string? CardCode,
    IReadOnlyList<ValidatePurchaseOrderLineDto> Lines
);

public sealed record ValidatePurchaseOrderLineDto(
    string ItemCode,
    string WarehouseCode
);

public sealed class ValidatePurchaseOrderRequestValidator : AbstractValidator<ValidatePurchaseOrderRequest>
{
    public const int MaxLines = 500;

    public ValidatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.CardCode).MaximumLength(15).When(x => x.CardCode is not null);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleFor(x => x.Lines)
            .Must(l => l is null || l.Count <= MaxLines)
            .WithMessage($"A maximum of {MaxLines} lines is allowed per batch.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemCode).NotEmpty().MaximumLength(50);
            line.RuleFor(l => l.WarehouseCode).NotEmpty().MaximumLength(8);
        });
    }
}
