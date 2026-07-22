using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record CreateLiquidacionRequest(
    DateOnly DocDate,
    string? Comments,
    IReadOnlyList<LiquidacionLineDto> Lines
);

public sealed record LiquidacionLineDto(
    string ItemCode,
    decimal Quantity,
    string WarehouseCode
);

public sealed class CreateLiquidacionRequestValidator : AbstractValidator<CreateLiquidacionRequest>
{
    public CreateLiquidacionRequestValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemCode).NotEmpty().MaximumLength(20);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.WarehouseCode).NotEmpty().MaximumLength(8);
        });
    }
}
