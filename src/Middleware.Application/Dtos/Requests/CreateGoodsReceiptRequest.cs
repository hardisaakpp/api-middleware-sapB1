using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record CreateGoodsReceiptRequest(
    string VendorCode,
    DateOnly DocDate,
    string? Comments,
    IReadOnlyList<GoodsReceiptLineDto> Lines
);

public sealed record GoodsReceiptLineDto(
    string ItemCode,
    decimal Quantity,
    decimal UnitPrice,
    string WarehouseCode
);

public sealed class CreateGoodsReceiptRequestValidator : AbstractValidator<CreateGoodsReceiptRequest>
{
    public CreateGoodsReceiptRequestValidator()
    {
        RuleFor(x => x.VendorCode).NotEmpty().MaximumLength(15);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemCode).NotEmpty().MaximumLength(20);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.WarehouseCode).NotEmpty().MaximumLength(8);
        });
    }
}
