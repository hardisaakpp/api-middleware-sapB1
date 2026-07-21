using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record CreateStockTransferRequest(
    string FromWarehouse,
    string ToWarehouse,
    DateOnly DocDate,
    string? Comments,
    IReadOnlyList<StockTransferLineDto> Lines,
    // Solo aplican al InventoryTransferRequest (POST /stock-transfers/requests).
    // UOdc = nº de OC de origen (UDF U_ODC en SAP); Series = serie de numeración del documento.
    // El flujo Python que funcionaba los envía; se omiten del payload si vienen nulos.
    int? UOdc = null,
    int? Series = null
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
