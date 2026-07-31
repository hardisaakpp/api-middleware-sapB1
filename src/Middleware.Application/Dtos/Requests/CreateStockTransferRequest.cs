using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record CreateStockTransferRequest(
    string FromWarehouse,
    string ToWarehouse,
    string U_ORIGEN,
    string? Comments,
    int Series,
    IReadOnlyList<StockTransferRequestLineDto> StockTransferLines
);

public sealed record StockTransferRequestLineDto(
    string ItemCode,
    decimal Quantity,
    string WarehouseCode
);

public sealed class CreateStockTransferRequestValidator : AbstractValidator<CreateStockTransferRequest>
{
    public CreateStockTransferRequestValidator()
    {
        RuleFor(x => x.FromWarehouse).NotEmpty().MaximumLength(8);
        RuleFor(x => x.ToWarehouse).NotEmpty().MaximumLength(8);
        RuleFor(x => x.FromWarehouse).NotEqual(x => x.ToWarehouse)
            .WithMessage("FromWarehouse and ToWarehouse must be different.");
        RuleFor(x => x.U_ORIGEN).NotEmpty();
        RuleFor(x => x.Series).GreaterThan(0);
        RuleFor(x => x.StockTransferLines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.StockTransferLines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemCode).NotEmpty().MaximumLength(20);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.WarehouseCode).NotEmpty().MaximumLength(8);
        });
    }
}
