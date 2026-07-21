using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record ValidateWarehousesRequest(
    IReadOnlyList<string> WarehouseCodes
);

public sealed class ValidateWarehousesRequestValidator : AbstractValidator<ValidateWarehousesRequest>
{
    public const int MaxCodes = 200;

    public ValidateWarehousesRequestValidator()
    {
        RuleFor(x => x.WarehouseCodes).NotEmpty().WithMessage("At least one warehouse code is required.");
        RuleFor(x => x.WarehouseCodes)
            .Must(c => c is null || c.Count <= MaxCodes)
            .WithMessage($"A maximum of {MaxCodes} warehouse codes is allowed per batch.");
        RuleForEach(x => x.WarehouseCodes).NotEmpty().MaximumLength(8);
    }
}
