using System.Text.Json;
using FluentValidation;
using Middleware.Application.Idempotency;

namespace Middleware.Application.Dtos.Requests;

// Mirrors API_SLAYER's /api/Liquidaciones contract 1:1 (CreateLiquidacionRequest / LiquidacionLine).
// DocDate/DocDueDate/TaxDate and WithholdingTaxDataCollection are accepted for parity but ignored
// downstream, same as the source implementation (SapGateway always stamps the server's current date).
public sealed record CreateLiquidacionRequest(
    string CardCode = "",
    int? Series = null,
    string? DocDate = null,
    string? DocDueDate = null,
    string? TaxDate = null,
    string? Comments = null,
    string? DocType = null,
    string? DocCurrency = null,
    string? JournalMemo = null,
    string? U_COD_ST = null,
    string? U_tipo_comprob = null,
    string? U_ORIGEN = null,
    string? U_SER_EST = null,
    string? U_SER_PE = null,
    int? U_STATUS = null,
    List<Dictionary<string, JsonElement>>? WithholdingTaxDataCollection = null,
    IReadOnlyList<LiquidacionLineDto>? DocumentLines = null,
    // Clave de idempotencia opcional (UDF U_sap_id de OPCH, 60). Si no viene, el middleware genera un
    // GUID; ver docs/idempotencia-documentos.md.
    string? U_sap_id = null
);

public sealed record LiquidacionLineDto(
    string AccountCode,
    string ItemDescription,
    decimal UnitPrice,
    decimal Quantity = 1,
    string TaxCode = "IVA_0",
    string CostingCode = "",
    string CostingCode2 = "",
    string CostingCode3 = "",
    string? CostingCode4 = null,
    string? CostingCode5 = null
);

public sealed class CreateLiquidacionRequestValidator : AbstractValidator<CreateLiquidacionRequest>
{
    public CreateLiquidacionRequestValidator()
    {
        RuleFor(x => x.CardCode).NotEmpty();
        RuleFor(x => x.DocumentLines).NotEmpty();
        RuleFor(x => x.U_sap_id).MaximumLength(IdempotencyKey.MaxLength).Matches(IdempotencyKey.AllowedPattern)
            .WithMessage(IdempotencyKey.FormatMessage);
        RuleForEach(x => x.DocumentLines).ChildRules(line =>
        {
            line.RuleFor(l => l.AccountCode).NotEmpty();
            line.RuleFor(l => l.ItemDescription).NotEmpty();
        });
    }
}
