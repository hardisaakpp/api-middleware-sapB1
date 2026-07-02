using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record CreateStockTransferRequest(
    string FromWarehouse,
    string ToWarehouse,
    DateOnly DocDate,
    string? Comments,
    IReadOnlyList<StockTransferLineDto> Lines
);

public sealed record StockTransferLineDto(
    string ItemCode,
    decimal Quantity,
    string? ProjectCode = null
);

public sealed class CreateStockTransferRequestValidator : AbstractValidator<CreateStockTransferRequest>
{
    public CreateStockTransferRequestValidator()
    {
        RuleFor(x => x.FromWarehouse).NotEmpty().MaximumLength(8);
        RuleFor(x => x.ToWarehouse).NotEmpty().MaximumLength(8);
        RuleFor(x => x.FromWarehouse).NotEqual(x => x.ToWarehouse)
            .WithMessage("FromWarehouse and ToWarehouse must be different.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemCode).NotEmpty().MaximumLength(20);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });
    }
}
