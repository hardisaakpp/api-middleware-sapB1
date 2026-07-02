using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record CreateStockTransferWithBinsRequest(
    string FromWarehouse,
    string ToWarehouse,
    DateOnly DocDate,
    string? Comments,
    IReadOnlyList<StockTransferLineWithBinsDto> Lines
);

public sealed record StockTransferLineWithBinsDto(
    string ItemCode,
    decimal Quantity,
    string FromBinCode,
    string ToBinCode
);

public sealed class CreateStockTransferWithBinsRequestValidator : AbstractValidator<CreateStockTransferWithBinsRequest>
{
    public CreateStockTransferWithBinsRequestValidator()
    {
        RuleFor(x => x.FromWarehouse).NotEmpty().MaximumLength(8);
        RuleFor(x => x.ToWarehouse).NotEmpty().MaximumLength(8);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemCode).NotEmpty().MaximumLength(20);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.FromBinCode).NotEmpty();
            line.RuleFor(l => l.ToBinCode).NotEmpty();
        });
    }
}
