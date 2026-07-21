using FluentValidation;

namespace Middleware.Application.Dtos.Requests;

public sealed record UpdateInventoryTransferRequestStatusRequest(
    int DocEntry,
    string U_STATUS
);

public sealed class UpdateInventoryTransferRequestStatusRequestValidator : AbstractValidator<UpdateInventoryTransferRequestStatusRequest>
{
    private static readonly string[] AllowedStatuses = ["0", "1", "2"];

    public UpdateInventoryTransferRequestStatusRequestValidator()
    {
        RuleFor(x => x.DocEntry).GreaterThan(0);
        RuleFor(x => x.U_STATUS).Must(s => AllowedStatuses.Contains(s))
            .WithMessage("U_STATUS must be '0', '1' or '2'.");
    }
}
