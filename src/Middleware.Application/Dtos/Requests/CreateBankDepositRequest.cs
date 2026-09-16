using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

/// <summary>
/// Depósito bancario de efectivo (SAP B1 object ODPS, resource Service Layer "Deposits") —
/// distinto de <see cref="CreateDepositRequest"/>, que crea entradas de mercancías de
/// inventario (OIGN) y no debe confundirse con este vertical.
/// </summary>
public sealed record CreateBankDepositRequest(
    string CodigoAlmacen,
    DateOnly FechaCierreCaja,
    DateOnly FechaDepositoBanco,
    string CodigoCuentaSAP,
    string NumeroComprobante,
    decimal MontoDepositado,
    string Moneda,
    string TipoDeposito,
    string CuentaPuente,
    string Responsable,
    bool EsSobrante
);

public sealed class CreateBankDepositRequestValidator : AbstractValidator<CreateBankDepositRequest>
{
    public CreateBankDepositRequestValidator()
    {
        RuleFor(x => x.CodigoAlmacen).NotEmpty().MaximumLength(20);
        RuleFor(x => x.FechaCierreCaja).NotEqual(default(DateOnly));
        RuleFor(x => x.FechaDepositoBanco).NotEqual(default(DateOnly));
        RuleFor(x => x.CodigoCuentaSAP).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NumeroComprobante).NotEmpty().MaximumLength(50);
        RuleFor(x => x.MontoDepositado).GreaterThan(0);
        RuleFor(x => x.Moneda).NotEmpty().MaximumLength(3);
        RuleFor(x => x.TipoDeposito).NotEmpty();
        RuleFor(x => x.CuentaPuente).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Responsable).NotEmpty().MaximumLength(100);
    }
}
