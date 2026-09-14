using FluentValidation;
using Middleware.Application.Idempotency;
using Middleware.Domain.Enums;

namespace Middleware.Application.Dtos.Requests;

public sealed record CreateDepositRequest(
    DepositType Type,
    CompanyEnvironment Environment,
    DateOnly DocDate,
    string? Comments,
    IReadOnlyList<DepositLineDto> Lines,
    // Clave de idempotencia opcional. Solo se escribe en SAP (UDF U_sap_id de InventoryGenEntries)
    // si el módulo la envía; sin ella el comportamiento es el de siempre. Ver docs/idempotencia-documentos.md.
    string? U_sap_id = null
);

public sealed record DepositLineDto(
    string ItemCode,
    decimal Quantity,
    string WarehouseCode
);

public sealed class CreateDepositRequestValidator : AbstractValidator<CreateDepositRequest>
{
    public CreateDepositRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Environment).IsInEnum();
        RuleFor(x => x.U_sap_id).MaximumLength(IdempotencyKey.MaxLength).Matches(IdempotencyKey.AllowedPattern)
            .WithMessage(IdempotencyKey.FormatMessage);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemCode).NotEmpty().MaximumLength(20);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.WarehouseCode).NotEmpty().MaximumLength(8);
        });
    }
}
