using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record GetPurchaseOrdersPendientesRequest(DateTime Desde, DateTime Hasta, IReadOnlyList<string> WarehouseCodes);

public sealed class GetPurchaseOrdersPendientesRequestValidator : AbstractValidator<GetPurchaseOrdersPendientesRequest>
{
    public GetPurchaseOrdersPendientesRequestValidator()
    {
        RuleFor(x => x.WarehouseCodes).NotEmpty();
        RuleFor(x => x)
            .Must(x => x.Desde <= x.Hasta)
            .WithMessage("'Desde' no puede ser posterior a 'Hasta'.")
            .WithName("Hasta");
    }
}
