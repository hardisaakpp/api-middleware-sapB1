using FluentValidation;
using Middleware.Application.Idempotency;

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
    int? Series = null,
    // Tag de sistema origen, mismo UDF que el resto de los Create*Request (Item, Liquidacion,
    // PurchaseOrder, StockTransfer Direct/BasedOnDocument/WithBins) -- opcional en todos ellos.
    string? U_ORIGEN = null,
    // Clave de idempotencia opcional. Solo se escribe en SAP (UDF U_sap_id de OWTQ) si el módulo la
    // envía; sin ella el comportamiento es el de siempre. Ver docs/idempotencia-documentos.md.
    string? U_sap_id = null
);

public sealed record StockTransferLineDto(
    string ItemCode,
    decimal Quantity,
    string? ProjectCode = null,
    // Destino de esta línea en particular, si difiere del ToWarehouse del encabezado (mismo
    // campo que ya usan las variantes Direct/BasedOnDocument/WithBins). Opcional: el flujo que
    // ya funciona no lo envía -- todas sus líneas van al ToWarehouse del encabezado.
    string? WarehouseCode = null
);

public sealed class CreateStockTransferRequestValidator : AbstractValidator<CreateStockTransferRequest>
{
    public CreateStockTransferRequestValidator()
    {
        RuleFor(x => x.FromWarehouse).NotEmpty().MaximumLength(8);
        RuleFor(x => x.ToWarehouse).NotEmpty().MaximumLength(8);
        RuleFor(x => x.U_sap_id).MaximumLength(IdempotencyKey.MaxLength).Matches(IdempotencyKey.AllowedPattern)
            .WithMessage(IdempotencyKey.FormatMessage);
        RuleFor(x => x.FromWarehouse).NotEqual(x => x.ToWarehouse)
            .WithMessage("FromWarehouse and ToWarehouse must be different.");
        // Requeridos por el módulo de reposición de mercadería, el consumidor actual de este
        // endpoint (ver docs/CONTRATO_MIDDLEWARE.md, endpoint #9) -- no por el flujo Python de
        // mba3-consultas, que no los envía y sigue funcionando porque UOdc/ProjectCode (los
        // suyos) quedan opcionales.
        RuleFor(x => x.U_ORIGEN).NotEmpty();
        RuleFor(x => x.Series).NotNull().GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line is required.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemCode).NotEmpty().MaximumLength(20);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.WarehouseCode).MaximumLength(8);
        });
    }
}
