using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record CreateStockTransferBasedOnDocumentRequest(
    string? DocObjectCode,
    string? CardCode,
    string DocDate,
    string? TaxDate,
    string? Comments,
    string FromWarehouse,
    string ToWarehouse,
    string? U_ORIGEN,
    int? Series,
    int? PriceList,
    IReadOnlyList<StockTransferBasedOnDocumentLineDto> StockTransferLines
);

public sealed record StockTransferBasedOnDocumentLineDto(
    int? BaseType,
    int? BaseEntry,
    int? BaseLine,
    string ItemCode,
    decimal Quantity,
    string WarehouseCode
);

public sealed class CreateStockTransferBasedOnDocumentRequestValidator : AbstractValidator<CreateStockTransferBasedOnDocumentRequest>
{
    public CreateStockTransferBasedOnDocumentRequestValidator()
    {
        RuleFor(x => x.DocDate).NotEmpty();
        RuleFor(x => x.FromWarehouse).NotEmpty().MaximumLength(8);
        RuleFor(x => x.ToWarehouse).NotEmpty().MaximumLength(8);
        RuleFor(x => x.FromWarehouse).NotEqual(x => x.ToWarehouse)
            .WithMessage("FromWarehouse and ToWarehouse must be different.");
        RuleFor(x => x.StockTransferLines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.StockTransferLines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemCode).NotEmpty().MaximumLength(20);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.WarehouseCode).NotEmpty().MaximumLength(8);
        });
    }
}
