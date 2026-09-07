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
    string WarehouseCode,
    // Opcionales — si se envían, la línea se crea como "Copy From" de esa Purchase Order
    // (BaseType=22 en SAP) en vez de un GoodsReceipt suelto: reduce el pendiente de esa PO y
    // la deja cerrarse sola cuando se completa. Si se omiten (null), el comportamiento es
    // exactamente el mismo que antes de este campo (no rompe a quien ya use el endpoint).
    int? BaseEntry = null,
    int? BaseLine = null
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
            // Si viene uno, tiene que venir el otro — a medias no arma un BaseType válido.
            line.RuleFor(l => l.BaseLine)
                .NotNull().When(l => l.BaseEntry.HasValue)
                .WithMessage("BaseLine es obligatorio cuando se especifica BaseEntry.");
            line.RuleFor(l => l.BaseEntry)
                .NotNull().When(l => l.BaseLine.HasValue)
                .WithMessage("BaseEntry es obligatorio cuando se especifica BaseLine.");
        });
    }
}
