using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record CreateStockTransferWithBinsRequest(
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
    IReadOnlyList<StockTransferWithBinsLineDto> StockTransferLines
);

public sealed record StockTransferWithBinsLineDto(
    int? BaseType,
    int? BaseEntry,
    int? BaseLine,
    string ItemCode,
    decimal Quantity,
    string WarehouseCode,
    // Omitir (null) si la bodega de ese lado no maneja ubicaciones (bins). Una línea puede
    // traer entradas de un solo lado (origen o destino) o de ambos, según qué bodega tenga
    // bins habilitados — de ahí que sea opcional en vez de un par fijo de campos.
    IReadOnlyList<StockTransferBinAllocationDto>? StockTransferLinesBinAllocations
);

public sealed record StockTransferBinAllocationDto(
    int BinAbsEntry,
    decimal Quantity,
    // 1 = Entrada (bin destino), 2 = Salida (bin origen).
    int BinActionType
);

public sealed class CreateStockTransferWithBinsRequestValidator : AbstractValidator<CreateStockTransferWithBinsRequest>
{
    public CreateStockTransferWithBinsRequestValidator()
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
            line.RuleForEach(l => l.StockTransferLinesBinAllocations).ChildRules(bin =>
            {
                bin.RuleFor(b => b.BinAbsEntry).GreaterThan(0);
                bin.RuleFor(b => b.Quantity).GreaterThan(0);
                bin.RuleFor(b => b.BinActionType).Must(v => v is 1 or 2)
                    .WithMessage("BinActionType must be 1 (Entrada) or 2 (Salida).");
            });
        });
    }
}
