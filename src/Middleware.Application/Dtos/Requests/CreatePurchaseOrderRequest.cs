using FluentValidation;
using Middleware.Application.Idempotency;

namespace Middleware.Application.Dtos.Requests;

public sealed record CreatePurchaseOrderRequest(
    string CardCode,
    DateOnly DocDate,
    DateOnly DocDueDate,
    DateOnly TaxDate,
    string? Comments,
    string? U_ORIGEN,
    string? U_USUARIO,
    DateOnly? U_FECHA,
    DateOnly? U_FE_ETG,
    string? U_LOTE,
    IReadOnlyList<CreatePurchaseOrderLineDto> Lines,
    // Clave de idempotencia opcional (UDF U_sap_id de OPOR, 60). Si no viene, el middleware genera un
    // GUID; ver docs/idempotencia-documentos.md.
    string? U_sap_id = null
);

public sealed record CreatePurchaseOrderLineDto(
    string ItemCode,
    decimal Quantity,
    decimal UnitPrice,
    string WarehouseCode
);

public sealed class CreatePurchaseOrderRequestValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    public CreatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.CardCode).NotEmpty().MaximumLength(15);
        RuleFor(x => x.U_sap_id).MaximumLength(IdempotencyKey.MaxLength).Matches(IdempotencyKey.AllowedPattern)
            .WithMessage(IdempotencyKey.FormatMessage);
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
